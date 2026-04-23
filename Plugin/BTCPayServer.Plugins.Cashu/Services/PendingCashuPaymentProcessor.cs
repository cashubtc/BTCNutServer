using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Plugins.Cashu.PaymentMethod;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using System.Collections.Generic;
using BTCPayServer.Client.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Cashu.Services;

public class PendingCashuPaymentProcessor(
    InvoiceRepository invoiceRepository,
    PaymentService paymentService,
    StoreRepository storeRepository,
    PaymentMethodHandlerDictionary handlers,
    LightningClientFactoryService lightningClientFactoryService,
    IOptions<LightningNetworkOptions> lightningNetworkOptions,
    CashuPaymentMethodHandler cashuPaymentMethodHandler,
    ILogger<PendingCashuPaymentProcessor> logger) : IHostedService
{
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan WaitTimeout { get; init; } = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, PendingCashuPayment> _payments = new();
    private CancellationTokenSource? _cts;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => RecoverPendingPaymentsAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public Task AddPayment(
        string storeId,
        string invoiceId,
        string paymentId,
        string lightningInvoiceId)
    {
        var pending = new PendingCashuPayment(storeId, invoiceId, paymentId, lightningInvoiceId);
        if (!_payments.TryAdd(paymentId, pending))
        {
            return Task.CompletedTask;
        }

        if (_cts != null)
        {
            _ = Task.Run(() => ProcessPendingPaymentAsync(pending, _cts.Token), _cts.Token);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task RecoverPendingPaymentsAsync(CancellationToken ct)
    {
        try
        {
            var invoices = await invoiceRepository.GetMonitoredInvoices(CashuPlugin.CashuPmid, includeNonActivated: true, cancellationToken: ct);
            foreach (var invoice in invoices)
            {
                foreach (var payment in invoice.GetPayments(false)
                             .Where(p => p.PaymentMethodId == CashuPlugin.CashuPmid && p.Status == PaymentStatus.Processing))
                {
                    if (payment.GetDetails<CashuPaymentData>(cashuPaymentMethodHandler) is not { PendingSettlement: true, LightningInvoiceId: { } lightningInvoiceId })
                    {
                        continue;
                    }

                    await AddPayment(invoice.StoreId, invoice.Id, payment.Id, lightningInvoiceId);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "(Cashu) Failed to recover pending Cashu payments");
        }
    }

    private async Task ProcessPendingPaymentAsync(PendingCashuPayment pending, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var invoice = await invoiceRepository.GetInvoice(pending.InvoiceId, true);
                if (invoice == null)
                {
                    _payments.TryRemove(pending.PaymentId, out _);
                    return;
                }

                var payment = invoice
                    .GetPayments(false)
                    .SingleOrDefault(p => p.Id == pending.PaymentId && p.PaymentMethodId == CashuPlugin.CashuPmid);

                if (payment == null || payment.Status == PaymentStatus.Settled)
                {
                    _payments.TryRemove(pending.PaymentId, out _);
                    return;
                }

                if (invoice.Status == InvoiceStatus.Settled)
                {
                    payment.Status = PaymentStatus.Settled;
                    await paymentService.UpdatePayments(new List<PaymentEntity> { payment });
                    _payments.TryRemove(pending.PaymentId, out _);
                    return;
                }

                var store = await storeRepository.FindStore(pending.StoreId);
                if (store == null)
                {
                    logger.LogWarning("(Cashu) Store {StoreId} missing for pending Cashu payment {PaymentId}", pending.StoreId, pending.PaymentId);
                    _payments.TryRemove(pending.PaymentId, out _);
                    return;
                }

                var lightningClient = GetStoreLightningClient(store, cashuPaymentMethodHandler.Network);
                if (lightningClient == null)
                {
                    logger.LogDebug("(Cashu) Lightning client unavailable for store {StoreId}; retrying pending Cashu payment {PaymentId}", pending.StoreId, pending.PaymentId);
                    await Task.Delay(RetryDelay, ct);
                    continue;
                }

                var waitResult = await WaitForInvoiceAsync(lightningClient, pending.LightningInvoiceId, ct);
                if (waitResult == PendingLightningInvoiceState.Pending)
                {
                    await Task.Delay(RetryDelay, ct);
                    continue;
                }

                if (waitResult == PendingLightningInvoiceState.Paid)
                {
                    payment.Status = PaymentStatus.Settled;
                    await paymentService.UpdatePayments(new List<PaymentEntity> { payment });
                    await invoiceRepository.MarkInvoiceStatus(invoice.Id, InvoiceStatus.Settled);
                    logger.LogInformation("(Cashu) Finalized pending Cashu payment {PaymentId} for invoice {InvoiceId}", pending.PaymentId, pending.InvoiceId);
                }
                else
                {
                    logger.LogWarning("(Cashu) Pending Cashu payment {PaymentId} ended without settlement for invoice {InvoiceId}", pending.PaymentId, pending.InvoiceId);
                }

                _payments.TryRemove(pending.PaymentId, out _);
                return;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await Task.Delay(RetryDelay, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "(Cashu) Error while processing pending Cashu payment {PaymentId}; retrying", pending.PaymentId);
                await Task.Delay(RetryDelay, ct);
            }
        }
    }

    private async Task<PendingLightningInvoiceState> WaitForInvoiceAsync(
        ILightningClient lightningClient,
        string lightningInvoiceId,
        CancellationToken ct)
    {
        var invoice = await lightningClient.GetInvoice(lightningInvoiceId, ct);
        if (invoice?.Status == LightningInvoiceStatus.Paid)
        {
            return PendingLightningInvoiceState.Paid;
        }

        if (invoice?.Status == LightningInvoiceStatus.Expired)
        {
            return PendingLightningInvoiceState.Expired;
        }

        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waitCts.CancelAfter(WaitTimeout);

        try
        {
            using var listener = await lightningClient.Listen(waitCts.Token);
            while (true)
            {
                var paid = await listener.WaitInvoice(waitCts.Token);
                if (paid?.Id == lightningInvoiceId)
                {
                    return paid.Status switch
                    {
                        LightningInvoiceStatus.Paid => PendingLightningInvoiceState.Paid,
                        LightningInvoiceStatus.Expired => PendingLightningInvoiceState.Expired,
                        _ => PendingLightningInvoiceState.Pending
                    };
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return PendingLightningInvoiceState.Pending;
        }
    }

    private ILightningClient? GetStoreLightningClient(StoreData store, BTCPayNetwork network)
    {
        var lightningPmi = PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode);
        var lightningConfig = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(
            lightningPmi,
            handlers
        );

        return lightningConfig?.CreateLightningClient(
            network,
            lightningNetworkOptions.Value,
            lightningClientFactoryService
        );
    }

    private sealed record PendingCashuPayment(
        string StoreId,
        string InvoiceId,
        string PaymentId,
        string LightningInvoiceId);

    private enum PendingLightningInvoiceState
    {
        Pending,
        Paid,
        Expired
    }
}
