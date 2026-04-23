using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.enums;
using BTCPayServer.Plugins.Cashu.Data.Models;
using BTCPayServer.Plugins.Cashu.Errors;
using BTCPayServer.Plugins.Cashu.PaymentMethod;
using BTCPayServer.Plugins.Cashu.Wallets;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using DotNut;
using DotNut.Api;
using DotNut.ApiModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;

namespace BTCPayServer.Plugins.Cashu.Services;

public class CashuMeltHandler(StatefulWalletFactory statefulWalletFactory,
    IOptions<LightningNetworkOptions> lightningNetworkOptions,
    CashuPaymentMethodHandler handler,
    PaymentMethodHandlerDictionary handlers,
    LightningClientFactoryService lightningClientFactoryService,
    ILogger<CashuMeltHandler> logs,
    MintManager mintManager,
    CashuPaymentRegistrar cashuPaymentRegistrar,
    CashuDbContextFactory cashuDbContextFactory,
    PendingCashuPaymentProcessor pendingCashuPaymentProcessor
    )
{

    public async Task ExecuteAsync(
        CashuOperationContext opCtx,
        CancellationToken cancellationToken
    )
    {
        if (!opCtx.Wallet.HasLightningClient)
        {
            logs.LogDebug("(Cashu) Could not find lightning client for melt operation");
            throw new LightningUnavailableException();
        }
        var keysets = await opCtx.Wallet.GetKeysets();
        if (keysets == null)
        {
            throw new MintOperationException("No keysets found.");
        }

        opCtx.Token.Proofs = CashuTokenHelper.MapShortKeysetIds(opCtx.Token.Proofs, keysets.Select(k => k.Id).ToList());

        // Pre-validate keyset ownership before melt to avoid losing funds on conflict
        try
        {
            var inputKeysetIds = opCtx.Token.Proofs.Select(p => p.Id).Distinct().ToList();
            await mintManager.ValidateKeysetOwnership(opCtx.Token.Mint, inputKeysetIds);
        }
        catch (InvalidOperationException ex)
        {
            logs.LogDebug("(Cashu) Keyset ID conflict before melt: {Message}", ex.Message);
            throw new KeysetConflictException(ex.Message, ex);
        }

        var meltQuoteResponse = await opCtx.Wallet.CreateMaxMeltQuote(opCtx, keysets);
        if (!meltQuoteResponse.Success)
        {
            logs.LogDebug("(Cashu) Could not create melt quote: {Error}", meltQuoteResponse.Error?.Message);
            throw new MintOperationException("Could not create melt quote.", meltQuoteResponse.Error!);
        }

        if (
            !CashuUtils.ValidateFees(
                opCtx.Token.Proofs,
                opCtx.PaymentMethodConfig.FeeConfing,
                meltQuoteResponse.KeysetFee!.Value,
                (ulong)meltQuoteResponse.MeltQuote!.FeeReserve
            )
        )
        {
            logs.LogDebug(
                "(Cashu) Fees exceed limit. LN fee: {LnFee}, keyset fee: {KeysetFee}",
                (ulong)meltQuoteResponse.MeltQuote!.FeeReserve,
                meltQuoteResponse.KeysetFee!.Value
            );
            throw new FeesTooHighException($"LN fee ({(ulong)meltQuoteResponse.MeltQuote!.FeeReserve}) or keyset fee ({meltQuoteResponse.KeysetFee!.Value}) exceeds the configured limit.");
        }

        logs.LogDebug(
            "(Cashu) Melt started. Invoice: {InvoiceId}, LN fee: {LnFee}, keyset fee: {KeysetFee}",
            opCtx.Invoice.Id,
            meltQuoteResponse.MeltQuote.FeeReserve,
            meltQuoteResponse.KeysetFee
        );

        var meltResponse = await opCtx.Wallet.Melt(meltQuoteResponse.MeltQuote, opCtx.Token.Proofs);

        if (meltResponse.Success)
        {
            bool lnInvPaid;
            try
            {
                lnInvPaid = await opCtx.Wallet.ValidateLightningInvoicePaid(
                    meltQuoteResponse.Invoice?.Id
                );
            }
            catch (Exception ex)
            {
                logs.LogDebug(
                    "(Cashu) ValidateLightningInvoicePaid threw for invoice {InvoiceId}: {Error}. Treating payment as pending.",
                    opCtx.Invoice.Id, ex.Message
                );
                lnInvPaid = false;
            }

            if (!lnInvPaid)
            {
                logs.LogDebug(
                    "(Cashu) Melt quote paid but LN invoice is still pending for invoice {InvoiceId}. Registering pending payment.",
                    opCtx.Invoice.Id
                );
                var pendingPaymentId = await cashuPaymentRegistrar.RegisterPending(
                    opCtx,
                    meltQuoteResponse.Invoice!.Id,
                    meltQuoteResponse.Invoice?.Amount ?? opCtx.Value);
                await pendingCashuPaymentProcessor.AddPayment(
                    opCtx.Store.Id,
                    opCtx.Invoice.Id,
                    pendingPaymentId,
                    meltQuoteResponse.Invoice.Id);
                return;
            }

            var amountMelted = meltQuoteResponse.Invoice?.Amount ?? LightMoney.Zero;
            var overpaidFeesReturned = (meltResponse.ChangeProofs?.Select(p => p.Amount).Sum() ?? 0L) * opCtx.UnitValue;
            var amountPaid = amountMelted + overpaidFeesReturned;

            await cashuPaymentRegistrar.Register(opCtx, amountPaid);

            logs.LogDebug(
                "(Cashu) Melt success. Melted: {Melted} sat, fees returned: {FeesReturned} sat, total: {Total} sat",
                amountMelted.ToUnit(LightMoneyUnit.Satoshi),
                overpaidFeesReturned.ToUnit(LightMoneyUnit.Satoshi),
                amountPaid.ToUnit(LightMoneyUnit.Satoshi)
            );
            return;
        }

        if (meltResponse.Error is CashuProtocolException cpe)
        {
            logs.LogDebug("(Cashu) Melt protocol error: {Error}", meltResponse.Error.Message);
            throw new MintOperationException("Melt failed: " + cpe.Message, cpe);
        }

        if (meltResponse.Error is HttpRequestException)
        {
            var ftx = new FailedTransaction
            {
                StoreId = opCtx.Store.Id,
                InvoiceId = opCtx.Invoice.Id,
                LastRetried = DateTimeOffset.UtcNow,
                MintUrl = opCtx.Token.Mint,
                Unit = opCtx.Token.Unit,
                InputProofs = opCtx.Token.Proofs.ToArray(),
                InputAmount = opCtx.Token.SumProofs,
                OperationType = OperationType.Melt,
                OutputData = meltResponse.BlankOutputs,
                MeltDetails = new MeltDetails
                {
                    Expiry = DateTimeOffset.FromUnixTimeSeconds(
                        meltQuoteResponse.MeltQuote.Expiry ?? DateTime.UtcNow.UnixTimestamp()
                    ),
                    LightningInvoiceId = meltQuoteResponse.Invoice!.Id,
                    MeltQuoteId = meltResponse.Quote!.Quote,
                    // Assert status as pending, even if it's paid - lightning invoice has to be paid
                    Status = "PENDING",
                },
                RetryCount = 1,
            };
            try
            {
                //retry
                var state = await opCtx.Wallet.CheckTokenState(opCtx.Token.Proofs);
                if (state == StateResponseItem.TokenState.UNSPENT)
                {
                    throw new MintOperationException("Melt failed: tokens were not spent.");
                }

                var pollResult = await this.PollFailed(ftx, opCtx.Store, cancellationToken);

                switch (pollResult.State)
                {
                    case CashuPaymentState.Success:
                        // Melt completed at the mint (LN invoice paid + quote PAID).
                        // Persist change proofs (if any) and register the payment atomically,
                        // using the same deterministic paymentId the normal flow would produce.
                        if (pollResult.ResultProofs is { Count: > 0 })
                        {
                            await cashuPaymentRegistrar.AddProofsToDb(
                                pollResult.ResultProofs,
                                ftx.StoreId,
                                ftx.MintUrl,
                                ProofState.Available
                            );
                        }
                        var recoveredChange = (pollResult.ResultProofs?.Select(p => p.Amount).Sum() ?? 0L) * opCtx.UnitValue;
                        var recoveredAmount = (meltQuoteResponse.Invoice?.Amount ?? LightMoney.Zero) + recoveredChange;
                        await cashuPaymentRegistrar.Register(opCtx, recoveredAmount);
                        logs.LogDebug(
                            "(Cashu) Melt recovered on retry for invoice {InvoiceId}. Amount: {Amount} sat",
                            opCtx.Invoice.Id,
                            recoveredAmount.ToUnit(LightMoneyUnit.Satoshi)
                        );
                        return;

                    case CashuPaymentState.Failed:
                        throw new MintOperationException("Melt failed after retry.");

                    case CashuPaymentState.Pending:
                        // LN payment in flight at the mint. Persist ftx for recovery and mark the
                        // BTCPay payment as pending instead of surfacing a failure to the user.
                        ftx.Details = pollResult.Error?.Message ?? "Melt pending after retry";
                        await using (var db = cashuDbContextFactory.CreateContext())
                        {
                            await db.FailedTransactions.AddAsync(ftx, cancellationToken);
                            await db.SaveChangesAsync(cancellationToken);
                        }
                        logs.LogDebug(
                            "(Cashu) Melt pending after retry for invoice {InvoiceId}. Recovery state saved; registering pending payment.",
                            opCtx.Invoice.Id
                        );
                        var pendingPaymentId = await cashuPaymentRegistrar.RegisterPending(
                            opCtx,
                            ftx.MeltDetails!.LightningInvoiceId,
                            meltQuoteResponse.Invoice?.Amount ?? opCtx.Value);
                        await pendingCashuPaymentProcessor.AddPayment(
                            opCtx.Store.Id,
                            opCtx.Invoice.Id,
                            pendingPaymentId,
                            ftx.MeltDetails.LightningInvoiceId);
                        return;
                }
            }
            catch (HttpRequestException)
            {
                ftx.Details = "Network error during melt retry; mint unreachable";
                logs.LogDebug(
                    "(Cashu) Network error during melt for invoice {InvoiceId}. Saved as failed transaction.",
                    opCtx.Invoice.Id
                );
                await using (var db = cashuDbContextFactory.CreateContext())
                {
                    await db.FailedTransactions.AddAsync(ftx, cancellationToken);
                    await db.SaveChangesAsync(cancellationToken);
                }
                throw new CashuPaymentException(
                    $"There was a problem processing your request. Please contact the merchant with corresponding invoice Id: {opCtx.Invoice.Id}"
                );
            }
        }
        else if (meltResponse.ChangeProofs != null)
        {
            // melt succeeded at mint level (LN invoice paid) but SaveProofs failed due to db error.
            // changeProofs being populated means the mint completed the melt.
            bool lnInvPaid;
            try
            {
                lnInvPaid = await opCtx.Wallet.ValidateLightningInvoicePaid(
                    meltQuoteResponse.Invoice?.Id
                );
            }
            catch (Exception ex)
            {
                logs.LogDebug(
                    "(Cashu) ValidateLightningInvoicePaid threw for invoice {InvoiceId}: {Error}. Treating payment as pending.",
                    opCtx.Invoice.Id, ex.Message
                );
                lnInvPaid = false;
            }

            if (lnInvPaid)
            {
                await cashuPaymentRegistrar.Register(opCtx, meltQuoteResponse.Invoice?.Amount ?? LightMoney.Zero);

                logs.LogDebug(
                    "(Cashu) Melt succeeded but SaveProofs failed. Invoice: {InvoiceId}. Error: {Error}",
                    opCtx.Invoice.Id,
                    meltResponse.Error?.Message
                );
            }

            // Save FailedTransaction for lost change proofs
            var ftx = new FailedTransaction
            {
                StoreId = opCtx.Store.Id,
                InvoiceId = opCtx.Invoice.Id,
                LastRetried = DateTimeOffset.UtcNow,
                MintUrl = opCtx.Token.Mint,
                Unit = opCtx.Token.Unit,
                InputProofs = opCtx.Token.Proofs.ToArray(),
                InputAmount = opCtx.Token.SumProofs,
                OperationType = OperationType.Melt,
                OutputData = meltResponse.BlankOutputs,
                MeltDetails = new MeltDetails
                {
                    Expiry = DateTimeOffset.FromUnixTimeSeconds(
                        meltQuoteResponse.MeltQuote.Expiry ?? DateTime.UtcNow.UnixTimestamp()
                    ),
                    LightningInvoiceId = meltQuoteResponse.Invoice!.Id,
                    MeltQuoteId = meltResponse.Quote!.Quote,
                    Status = lnInvPaid ? "PAID" : "PENDING",
                },
                RetryCount = 1,
                Details = $"Melt succeeded at mint but local SaveProofs failed. Error: {meltResponse.Error?.Message}",
            };
            await using var ctx = cashuDbContextFactory.CreateContext();
            ctx.FailedTransactions.Add(ftx);
            await ctx.SaveChangesAsync();

            if (!lnInvPaid)
            {
                var pendingPaymentId = await cashuPaymentRegistrar.RegisterPending(
                    opCtx,
                    meltQuoteResponse.Invoice!.Id,
                    meltQuoteResponse.Invoice?.Amount ?? opCtx.Value);
                await pendingCashuPaymentProcessor.AddPayment(
                    opCtx.Store.Id,
                    opCtx.Invoice.Id,
                    pendingPaymentId,
                    meltQuoteResponse.Invoice.Id);
            }
        }
        else
        {
            logs.LogDebug("(Cashu) Unexpected melt error: {Error}", meltResponse.Error?.Message);
            throw new MintOperationException("Melt failed unexpectedly.", meltResponse.Error!);
        }
    }

    public async Task<PollResult> PollFailed(
        FailedTransaction ftx,
        StoreData storeData,
        CancellationToken cts = default
    )
    {
        if (ftx.OperationType != OperationType.Melt || ftx.MeltDetails == null)
        {
            throw new InvalidOperationException($"Unexpected operation type: {ftx.OperationType}");
        }
        var lightningClient = GetStoreLightningClient(storeData, handler.Network);
        var lnInvoice = await lightningClient.GetInvoice(ftx.MeltDetails.LightningInvoiceId, cts);

        if (lnInvoice == null)
            return new PollResult() { State = CashuPaymentState.Pending };

        if (lnInvoice.Status == LightningInvoiceStatus.Expired)
        {
            return new PollResult() { State = CashuPaymentState.Failed };
        }

        //If the invoice is paid, we should process the payment, even though if change isn't received.
        if (lnInvoice.Status == LightningInvoiceStatus.Paid)
        {
            var wallet = await statefulWalletFactory.CreateAsync(
                ftx.StoreId,
                ftx.MintUrl,
                ftx.Unit
            );

            try
            {
                var meltQuoteState = await wallet.CheckMeltQuoteState(
                    ftx.MeltDetails.MeltQuoteId,
                    cts
                );
                var status = CompareMeltQuotes(ftx.MeltDetails, meltQuoteState);
                if (status == CashuPaymentState.Success)
                {
                    //Change won't be always present
                    if (meltQuoteState.Change == null || meltQuoteState.Change.Length == 0)
                    {
                        return new PollResult() { State = CashuPaymentState.Success };
                    }
                    var firstChange = meltQuoteState.Change.FirstOrDefault();
                    if (firstChange == null)
                    {
                        return new PollResult() { State = CashuPaymentState.Success };
                    }
                    var keys = await wallet.GetKeys(firstChange.Id);
                    if (keys == null)
                    {
                        // Can't unblind change proofs without keys — retry when mint is reachable.
                        return new PollResult() { State = CashuPaymentState.Pending };
                    }
                    var proofs = DotNut.Abstractions.Utils.ConstructProofsFromPromises(
                        meltQuoteState.Change.ToList(),
                        ftx.OutputData,
                        keys
                    );
                    return new PollResult()
                    {
                        State = CashuPaymentState.Success,
                        ResultProofs = proofs,
                    };
                }

                return new PollResult() { State = status };
            }
            catch (HttpRequestException ex)
            {
                return new PollResult() { State = CashuPaymentState.Pending, Error = ex };
            }
        }

        if (lnInvoice.Status == LightningInvoiceStatus.Expired)
        {
            return new PollResult() { State = CashuPaymentState.Failed };
        }

        return new PollResult() { State = CashuPaymentState.Pending };
    }

    private ILightningClient GetStoreLightningClient(StoreData store, BTCPayNetwork network)
    {
        var lightningPmi = PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode);

        var lightningConfig = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(
            lightningPmi,
            handlers
        );

        if (lightningConfig == null)
            throw new PaymentMethodUnavailableException("Lightning not configured");

        return lightningConfig.CreateLightningClient(
            network,
            lightningNetworkOptions.Value,
            lightningClientFactoryService
        );
    }

    private CashuPaymentState CompareMeltQuotes(
        MeltDetails prevMeltState,
        PostMeltQuoteBolt11Response currentMeltState
    )
    {
        //Shouldn't happen
        if (prevMeltState.Status == "PAID")
        {
            return CashuPaymentState.Success;
        }
        // paid, should check the invoice state in next
        if (currentMeltState.State == "PAID")
        {
            return CashuPaymentState.Success;
        }
        // if it was pending and now it's not, we should treat it as it never happened. Proofs weren't spent.
        if (prevMeltState.Status == "PENDING")
        {
            if (currentMeltState.State == "UNPAID")
            {
                return CashuPaymentState.Failed;
            }
        }

        if (currentMeltState.State == "PENDING")
        {
            //isn't paid, but it will be
            return CashuPaymentState.Pending;
        }

        //if it's unpaid and it was unpaid let's assume it's pending untill timeout
        if (currentMeltState.State == "UNPAID")
        {
            return prevMeltState.Expiry <= new DateTimeOffset(DateTime.UtcNow)
                ? CashuPaymentState.Failed
                : CashuPaymentState.Pending;
        }

        return CashuPaymentState.Failed;
    }
}
