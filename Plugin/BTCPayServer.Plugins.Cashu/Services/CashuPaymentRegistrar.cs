using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.enums;
using BTCPayServer.Plugins.Cashu.Data.Models;
using BTCPayServer.Plugins.Cashu.PaymentMethod;
using BTCPayServer.Services.Invoices;
using DotNut;
using DotNut.JsonConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BTCPayServer.Plugins.Cashu.Services;

public class CashuPaymentRegistrar(
    InvoiceRepository invoiceRepository,
    CashuPaymentMethodHandler handler,
    PaymentService paymentService,
    ILogger<CashuPaymentRegistrar> logs,
    MintManager mintManager,
    CashuDbContextFactory cashuDbContextFactory
    )
{

    private static readonly JsonSerializerOptions SecretJsonOptions = new()
    {
        Converters = { new SecretJsonConverter() }
    };

    public Task Register(
        CashuOperationContext ctx,
        LightMoney value = null,
        bool markPaid = true
    ) => Register(
        ctx.Invoice,
        value ?? ctx.Value,
        BuildPaymentId(ctx.Invoice.Id, ctx.Token.Proofs),
        markPaid
    );
    /// <summary>
    /// Registers a Cashu payment for the invoice. Idempotent: identified by deterministic paymentId
    /// so retries, parallel callers, or partial-failure recoveries never double-count.
    /// </summary>
    public async Task Register(
        InvoiceEntity invoice,
        LightMoney value,
        string paymentId,
        bool markPaid = true
    )
    {
        // Re-read invoice to avoid acting on stale payment list (caller's snapshot may miss a
        // concurrent registration from another thread / manual retry / poller).
        var fresh = await invoiceRepository.GetInvoice(invoice.Id, true) ?? invoice;
        var alreadyRegistered = fresh
            .GetPayments(false)
            .Any(p => p.Id == paymentId);

        if (!alreadyRegistered)
        {
            //set payment method fee to 0 so it won't be added to due for second time
            var prompt = fresh.GetPaymentPrompt(CashuPlugin.CashuPmid);
            if (prompt != null && prompt.PaymentMethodFee != 0.0m)
            {
                prompt.PaymentMethodFee = 0.0m;
                await invoiceRepository.UpdatePrompt(fresh.Id, prompt);
            }

            var paymentData = new PaymentData
            {
                Id = paymentId,
                Created = DateTimeOffset.UtcNow,
                Status = PaymentStatus.Processing,
                Currency = "BTC",
                InvoiceDataId = fresh.Id,
                Amount = value.ToDecimal(LightMoneyUnit.BTC),
                PaymentMethodId = handler.PaymentMethodId.ToString(),
            }.Set(fresh, handler, new CashuPaymentData());

            // AddPayment swallows DbUpdateException on duplicate PK (Id, PaymentMethodId),
            // returning null — so simultaneous callers are safe even if the pre-check missed.
            await paymentService.AddPayment(paymentData);
        }

        if (markPaid && fresh.Status != InvoiceStatus.Settled)
        {
            await invoiceRepository.MarkInvoiceStatus(fresh.Id, InvoiceStatus.Settled);
        }
    }

    /// <summary>
    /// Registers payment for a recovered FailedTransaction. Idempotent.
    /// Throws on transient errors (network, DB) so the caller keeps ftx unresolved and retries later.
    /// Returns a terminal classification for permanent outcomes.
    /// </summary>
    public async Task<FtxPaymentRegistrationResult> RegisterPaymentForFailedTx(
        FailedTransaction ftx,
        CancellationToken ct = default
    )
    {
        var invoice = await invoiceRepository.GetInvoice(ftx.InvoiceId, true);
        if (invoice is null)
            return FtxPaymentRegistrationResult.InvoiceMissing;

        if (invoice.Status == InvoiceStatus.Settled)
            return FtxPaymentRegistrationResult.Registered;

        // GetTokenSatRate throws HttpRequestException on mint outage — propagate so ftx stays
        // unresolved and the poller retries. Don't swallow as before (that hid lost payments).
        var singleUnitPrice = await CashuUtils.GetTokenSatRate(
            ftx.MintUrl,
            ftx.Unit,
            handler.Network.NBitcoinNetwork
        );

        var isOld = ftx is { InputAmount: 0, InputProofsJson: null };
        LightMoney? paymentAmount = isOld switch
        {
            true when invoice.Currency is "SATS" => LightMoney.Satoshis(invoice.Price),
            true when invoice.Currency is "BTC" => LightMoney.Coins(invoice.Price),
            false => ftx.InputAmount * singleUnitPrice,
            _ => null
        };

        if (paymentAmount is null)
        {
            logs.LogError(
                "(Cashu) Can't determine payment amount for failed tx {Id}: InputAmount={InputAmount} currency={Currency}. Manual registration required.",
                ftx.Id, ftx.InputAmount, invoice.Currency
            );
            return FtxPaymentRegistrationResult.UnresolvableAmount;
        }

        await Register(invoice, paymentAmount, BuildPaymentIdForFtx(ftx));
        return FtxPaymentRegistrationResult.Registered;
    }

    /// <summary>
    /// Deterministic payment identifier derived from invoice + input proof secrets.
    /// Same (invoice, token) always yields the same ID so retries are idempotent at the DB layer.
    /// </summary>
    internal static string BuildPaymentId(string invoiceId, IEnumerable<Proof> inputProofs)
    {
        var secrets = inputProofs
            .Select(p => JsonSerializer.Serialize(p.Secret, SecretJsonOptions))
            .OrderBy(s => s, StringComparer.Ordinal);
        var payload = $"{invoiceId}|{string.Join(",", secrets)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return "cashu-" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Deterministic payment identifier for a FailedTransaction. Prefers hash of InputProofs for
    /// parity with the happy-path ID (so the same logical payment always maps to one record),
    /// falls back to ftx.Id for legacy ftx rows missing InputProofsJson.
    /// </summary>
    internal static string BuildPaymentIdForFtx(FailedTransaction ftx)
    {
        var inputs = ftx.InputProofs;
        return inputs.Length > 0
            ? BuildPaymentId(ftx.InvoiceId, inputs)
            : "cashu-ftx-" + ftx.Id.ToString("N");
    }

    public async Task AddProofsToDb(
        IEnumerable<Proof>? proofs,
        string storeId,
        string mintUrl,
        ProofState status
    )
    {
        if (proofs == null)
        {
            return;
        }

        var enumerable = proofs as Proof[] ?? proofs.ToArray();

        if (enumerable.Length == 0)
        {
            return;
        }

        await mintManager.GetOrCreateMint(mintUrl);

        await using var dbContext = cashuDbContextFactory.CreateContext();
        var dbProofs = StoredProof.FromBatch(enumerable, storeId, status).ToArray();
        dbContext.Proofs.AddRange(dbProofs);

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505", ConstraintName: "IX_Proofs_Secret" })
        {
            // Retry scenario: some or all proofs already exist, insert individually, skipping duplicates
            dbContext.ChangeTracker.Clear();
            foreach (var proof in dbProofs)
            {
                try
                {
                    dbContext.Proofs.Add(proof);
                    await dbContext.SaveChangesAsync();
                }
                catch (DbUpdateException inner) when (inner.InnerException is PostgresException { SqlState: "23505", ConstraintName: "IX_Proofs_Secret" })
                {
                    dbContext.ChangeTracker.Clear();
                }
            }
        }
    }

    public enum FtxPaymentRegistrationResult
    {
        /// <summary>Payment was added or already present; invoice is (or will be) settled.</summary>
        Registered,
        /// <summary>Invoice is gone. Nothing we can do — treat as resolved so poller stops retrying.</summary>
        InvoiceMissing,
        /// <summary>Input amount + currency combination can't produce a payment amount. Manual intervention required.</summary>
        UnresolvableAmount,
    }

}