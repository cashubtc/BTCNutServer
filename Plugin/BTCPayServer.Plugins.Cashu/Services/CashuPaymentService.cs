using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Logging;
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
using BTCPayServer.Services.Stores;
using DotNut;
using DotNut.Api;
using DotNut.ApiModels;
using DotNut.JsonConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using Npgsql;

using InvoiceStatus = BTCPayServer.Client.Models.InvoiceStatus;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Cashu.Services;

public record CashuOperationContext(
    StatefulWallet Wallet,
    InvoiceEntity Invoice,
    StoreData Store,
    CashuUtils.SimplifiedCashuToken Token,
    CashuPaymentMethodConfig PaymentMethodConfig,
    LightMoney UnitValue,
    LightMoney Value);

public class CashuPaymentService(
    StoreRepository storeRepository,
    InvoiceRepository invoiceRepository,
    PaymentService paymentService,
    CashuPaymentMethodHandler handler,
    PaymentMethodHandlerDictionary handlers,
    LightningClientFactoryService lightningClientFactoryService,
    IOptions<LightningNetworkOptions> lightningNetworkOptions,
    CashuDbContextFactory cashuDbContextFactory,
    MintManager mintManager,
    StatefulWalletFactory statefulWalletFactory,
    Logs logs)
{

    /// <summary>
    /// Processing the payment from user input;
    /// </summary>
    /// <param name="token">v4 Cashu Token</param>
    /// <param name="invoiceId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task ProcessPaymentAsync(
        CashuToken token,
        string invoiceId,
        CancellationToken cancellationToken = default
    )
    {
        logs.PayServer.LogDebug("(Cashu) Processing payment for invoice {InvoiceId}", invoiceId);

        var invoice = await invoiceRepository.GetInvoice(invoiceId, true);
        if (invoice == null)
        {
            throw new CashuPaymentException("Invalid invoice");
        }

        var storeData = await storeRepository.FindStore(invoice.StoreId);
        if (storeData == null)
        {
            throw new InvalidOperationException("Invalid store"); // should never happen 
        }

        var cashuPaymentMethodConfig = storeData.GetPaymentMethodConfig<CashuPaymentMethodConfig>(
            CashuPlugin.CashuPmid,
            handlers
        );
        if (cashuPaymentMethodConfig == null)
        {
            logs.PayServer.LogDebug("(Cashu) Couldn't get Cashu Payment method config for invoice {InvoiceId}", invoiceId);
            throw new CashuPaymentException("Couldn't process the payment. Token wasn't spent.");
        }

        var network = handler.Network;
        LightMoney singleUnitSatoshiWorth;
        try
        {
            singleUnitSatoshiWorth = await CashuUtils.GetTokenSatRate(
                token,
                network.NBitcoinNetwork
            );
        }
        catch (HttpRequestException ex)
        {
            var mintUrl = token.Tokens.FirstOrDefault()?.Mint ?? "unknown mint";
            logs.PayServer.LogDebug("(Cashu) Couldn't connect to mint {MintUrl} for invoice {InvoiceId}", mintUrl, invoiceId);
            throw new MintUnreachableException(mintUrl, ex);
        }
        catch (CashuProtocolException ex)
        {
            logs.PayServer.LogDebug("(Cashu) Protocol error for invoice {InvoiceId}: {Error}", invoiceId, ex.Message);
            throw new CashuPaymentException(ex.Message, ex);
        }

        var invoiceAmount = Money.Coins(
            invoice.GetPaymentPrompt(CashuPlugin.CashuPmid)?.Calculate().Due ?? invoice.Price
        );

        var simplifiedToken = CashuUtils.SimplifyToken(token);
        var providedAmount = simplifiedToken.SumProofs * singleUnitSatoshiWorth;

        if (providedAmount < invoiceAmount)
        {
            logs.PayServer.LogDebug(
                "(Cashu) Insufficient token worth for invoice {InvoiceId}. Expected {ExpectedSats}, got {ProvidedSats}",
                invoiceId,
                invoiceAmount.Satoshi,
                providedAmount.ToUnit(LightMoneyUnit.Satoshi)
            );
            throw new InsufficientFundsException(invoiceAmount.Satoshi, (long)providedAmount.ToUnit(LightMoneyUnit.Satoshi));
        }

        logs.PayServer.LogDebug(
            "(Cashu) Processing payment. Invoice: {InvoiceId}, Store: {StoreId}, Amount: {AmountSats} sat",
            invoiceId,
            invoice.StoreId,
            invoiceAmount.Satoshi
        );

        var wallet = await statefulWalletFactory.CreateAsync(
            storeData.Id,
            simplifiedToken.Mint,
            simplifiedToken.Unit
        );
        var ctx = new CashuOperationContext(
            wallet,
            invoice,
            storeData,
            simplifiedToken,
            cashuPaymentMethodConfig,
            singleUnitSatoshiWorth,
            providedAmount);

        var trusted = cashuPaymentMethodConfig.TrustedMintsUrls.Contains(simplifiedToken.Mint);
        var (melt, swap) = cashuPaymentMethodConfig.PaymentModel switch
        {
            CashuPaymentModel.AutoConvert => (true, false),
            CashuPaymentModel.HoldWhenTrusted => (!trusted, trusted),
            CashuPaymentModel.TrustedMintsOnly => (false, trusted),
            _ => throw new NotSupportedException(cashuPaymentMethodConfig.PaymentModel.ToString())
        };

        if (swap)
        {
            await HandleSwapOperation(ctx, cancellationToken);
        }
        else if (melt)
        {
            await HandleMeltOperation(ctx, cancellationToken);
        }
        else
        {
            throw new UntrustedMintException(ctx.Token.Mint);
        }
    }


    private async Task HandleSwapOperation(
        CashuOperationContext ctx,
        CancellationToken cts = default
    )
    {
        // Pre-validate keyset ownership before swap to avoid losing funds on conflict
        try
        {
            var inputKeysetIds = ctx.Token.Proofs.Select(p => p.Id).Distinct().ToList();
            await mintManager.ValidateKeysetOwnership(ctx.Token.Mint, inputKeysetIds);
        }
        catch (InvalidOperationException ex)
        {
            logs.PayServer.LogDebug("(Cashu) Keyset ID conflict before swap: {Message}", ex.Message);
            throw new KeysetConflictException(ex.Message, ex);
        }

        var keysets = await ctx.Wallet.GetKeysets();
        if (keysets == null)
        {
            throw new MintOperationException("No keysets found.");
        }

        ctx.Token.Proofs = CashuTokenHelper.MapShortKeysetIds(ctx.Token.Proofs, keysets.Select(k => k.Id).ToList());

        if (!CashuUtils.ValidateFees(ctx.Token.Proofs, ctx.PaymentMethodConfig.FeeConfing, keysets, out var keysetFee))
        {
            logs.PayServer.LogDebug("(Cashu) Keyset fees exceed limit: {Fee}. Token wasn't spent.", keysetFee);
            throw new FeesTooHighException($"Keyset fees ({keysetFee}) exceed the configured limit.");
        }

        logs.PayServer.LogDebug(
            "(Cashu) Swap initiated. Mint: {MintUrl}, InputProofs: {ProofCount}, Fee: {FeeSats} sat",
            ctx.Token.Mint,
            ctx.Token.Proofs.Count,
            keysetFee
        );

        var swapResult = await ctx.Wallet.Receive(ctx.Token.Proofs, keysetFee);

        //handle swap errors
        if (!swapResult.Success)
        {
            switch (swapResult.Error)
            {
                case CashuProtocolException cpe:
                    throw new MintOperationException(cpe.Message, cpe);

                case CashuPaymentException cpe:
                    throw cpe;

                case HttpRequestException:
                    {
                        var ftx = new FailedTransaction()
                        {
                            InvoiceId = ctx.Invoice.Id,
                            StoreId = ctx.Invoice.StoreId,
                            LastRetried = DateTimeOffset.UtcNow,
                            MintUrl = ctx.Token.Mint,
                            InputProofs = ctx.Token.Proofs.ToArray(),
                            InputAmount = ctx.Token.SumProofs,
                            OperationType = OperationType.Swap,
                            OutputData = swapResult.ProvidedOutputs,
                            Unit = ctx.Token.Unit,
                            RetryCount = 0,
                            Details = "Connection with mint broken while swap",
                        };
                        var pollResult = await PollFailedSwap(ftx, ctx.Store, cts);

                        if (!pollResult.Success)
                        {
                            ftx.RetryCount += 1;
                            ftx.LastRetried = DateTimeOffset.UtcNow;
                            await using (var db = cashuDbContextFactory.CreateContext())
                            {
                                await db.FailedTransactions.AddAsync(ftx, cts);
                                await db.SaveChangesAsync(cts);
                            }
                            logs.PayServer.LogDebug(
                                "(Cashu) Transaction {InvoiceId} failed: broken connection with mint. Saved as failed transaction.",
                                ctx.Invoice.Id
                            );
                            // surface failure to the caller — without this the UI reports success
                            // while the merchant's invoice remains unsettled.
                            throw new CashuPaymentException(
                                $"There was a problem processing your request. Please contact the merchant with corresponding invoice Id: {ctx.Invoice.Id}"
                            );
                        }

                        await AddProofsToDb(
                            pollResult.ResultProofs!,
                            ftx.StoreId,
                            ftx.MintUrl,
                            ProofState.Available
                        );
                        await RegisterCashuPayment(ctx);
                        return;
                    }
                default:
                    logs.PayServer.LogDebug("(Cashu) Swap failed: {Error}", swapResult.Error?.Message);
                    throw new MintOperationException("Swap failed.", swapResult.Error!);
            }
        }

        var returnedAmount = swapResult.ResultProofs!.Select(p => p.Amount).Sum();
        logs.PayServer.LogDebug("(Cashu) Swap success. {Amount} {Unit} received.", returnedAmount, ctx.Token.Unit);
        if (returnedAmount < ctx.Token.SumProofs - keysetFee)
        {
            var ftx = new FailedTransaction()
            {
                InvoiceId = ctx.Invoice.Id,
                StoreId = ctx.Invoice.StoreId,
                LastRetried = DateTimeOffset.UtcNow,
                MintUrl = ctx.Token.Mint,
                InputProofs = ctx.Token.Proofs.ToArray(),
                InputAmount = ctx.Token.SumProofs,
                OperationType = OperationType.Swap,
                OutputData = swapResult.ProvidedOutputs,
                Unit = ctx.Token.Unit,
                RetryCount = 0,
                Details =
                    "Mint Returned less signatures than was requested. Even though, merchant received the payment",
            };

            // Save FailedTransaction for manual recovery
            await using var dbCtx = cashuDbContextFactory.CreateContext();
            await dbCtx.FailedTransactions.AddAsync(ftx);
            await dbCtx.SaveChangesAsync();

            logs.PayServer.LogDebug(
                "(Cashu) Mint returned less signatures than requested for transaction {InvoiceId}. Saved as failed transaction for recovery.",
                ctx.Invoice.Id
            );
            //TODO: Pay partially or retry to recover missing proofs
        }
        await RegisterCashuPayment(ctx);
    }

    /// <summary>
    /// Handles melt operation with retry
    /// </summary>
    private async Task HandleMeltOperation(
        CashuOperationContext opCtx,
        CancellationToken cancellationToken
    )
    {
        if (!opCtx.Wallet.HasLightningClient)
        {
            logs.PayServer.LogDebug("(Cashu) Could not find lightning client for melt operation");
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
            logs.PayServer.LogDebug("(Cashu) Keyset ID conflict before melt: {Message}", ex.Message);
            throw new KeysetConflictException(ex.Message, ex);
        }

        var meltQuoteResponse = await opCtx.Wallet.CreateMaxMeltQuote(opCtx, keysets);
        if (!meltQuoteResponse.Success)
        {
            logs.PayServer.LogDebug("(Cashu) Could not create melt quote: {Error}", meltQuoteResponse.Error?.Message);
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
            logs.PayServer.LogDebug(
                "(Cashu) Fees exceed limit. LN fee: {LnFee}, keyset fee: {KeysetFee}",
                (ulong)meltQuoteResponse.MeltQuote!.FeeReserve,
                meltQuoteResponse.KeysetFee!.Value
            );
            throw new FeesTooHighException($"LN fee ({(ulong)meltQuoteResponse.MeltQuote!.FeeReserve}) or keyset fee ({meltQuoteResponse.KeysetFee!.Value}) exceeds the configured limit.");
        }

        logs.PayServer.LogDebug(
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
                logs.PayServer.LogDebug(
                    "(Cashu) ValidateLightningInvoicePaid threw for invoice {InvoiceId}: {Error}. Saving ftx for poller.",
                    opCtx.Invoice.Id, ex.Message
                );
                lnInvPaid = false;
            }

            if (!lnInvPaid)
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
                        // if it's null it means it's already paid or expired
                        Expiry = DateTimeOffset.FromUnixTimeSeconds(
                            meltQuoteResponse.MeltQuote.Expiry ?? DateTime.UtcNow.UnixTimestamp()
                        ),
                        LightningInvoiceId = meltQuoteResponse.Invoice!.Id,
                        MeltQuoteId = meltResponse.Quote!.Quote,
                        // Assert status as pending, even if it's paid - lightning invoice has to be paid
                        Status = "PENDING",
                    },
                    RetryCount = 1,
                    Details =
                        "Mint marked melt quote as paid, but lightning invoice state is unknown or still unpaid.",
                };
                await using var ctx = cashuDbContextFactory.CreateContext();
                ctx.FailedTransactions.Add(ftx);
                await ctx.SaveChangesAsync();
                logs.PayServer.LogDebug(
                    "(Cashu) Melt quote paid but LN invoice unpaid for invoice {InvoiceId}. Saved as failed transaction.",
                    opCtx.Invoice.Id
                );
                throw new CashuPaymentException(
                    $"There was a problem processing your request. Please contact the merchant with corresponding invoice Id: {opCtx.Invoice.Id}"
                );
            }

            var amountMelted = meltQuoteResponse.Invoice?.Amount ?? LightMoney.Zero;
            var overpaidFeesReturned = (meltResponse.ChangeProofs?.Select(p => p.Amount).Sum() ?? 0L) * opCtx.UnitValue;
            var amountPaid = amountMelted + overpaidFeesReturned;

            await RegisterCashuPayment(opCtx, amountPaid);

            logs.PayServer.LogDebug(
                "(Cashu) Melt success. Melted: {Melted} sat, fees returned: {FeesReturned} sat, total: {Total} sat",
                amountMelted.ToUnit(LightMoneyUnit.Satoshi),
                overpaidFeesReturned.ToUnit(LightMoneyUnit.Satoshi),
                amountPaid.ToUnit(LightMoneyUnit.Satoshi)
            );
            return;
        }

        if (meltResponse.Error is CashuProtocolException cpe)
        {
            logs.PayServer.LogDebug("(Cashu) Melt protocol error: {Error}", meltResponse.Error.Message);
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

                var pollResult = await PollFailedMelt(ftx, opCtx.Store, cancellationToken);

                switch (pollResult.State)
                {
                    case CashuPaymentState.Success:
                        // Melt completed at the mint (LN invoice paid + quote PAID).
                        // Persist change proofs (if any) and register the payment atomically,
                        // using the same deterministic paymentId the normal flow would produce.
                        if (pollResult.ResultProofs is { Count: > 0 })
                        {
                            await AddProofsToDb(
                                pollResult.ResultProofs,
                                ftx.StoreId,
                                ftx.MintUrl,
                                ProofState.Available
                            );
                        }
                        var recoveredChange = (pollResult.ResultProofs?.Select(p => p.Amount).Sum() ?? 0L) * opCtx.UnitValue;
                        var recoveredAmount = (meltQuoteResponse.Invoice?.Amount ?? LightMoney.Zero) + recoveredChange;
                        await RegisterCashuPayment(opCtx, recoveredAmount);
                        logs.PayServer.LogDebug(
                            "(Cashu) Melt recovered on retry for invoice {InvoiceId}. Amount: {Amount} sat",
                            opCtx.Invoice.Id,
                            recoveredAmount.ToUnit(LightMoneyUnit.Satoshi)
                        );
                        return;

                    case CashuPaymentState.Failed:
                        throw new MintOperationException("Melt failed after retry.");

                    case CashuPaymentState.Pending:
                        // LN payment in flight at the mint. Persist ftx so the poller can finalize.
                        ftx.Details = pollResult.Error?.Message ?? "Melt pending after retry";
                        await using (var db = cashuDbContextFactory.CreateContext())
                        {
                            await db.FailedTransactions.AddAsync(ftx, cancellationToken);
                            await db.SaveChangesAsync(cancellationToken);
                        }
                        logs.PayServer.LogDebug(
                            "(Cashu) Melt pending after retry for invoice {InvoiceId}. Saved as failed transaction.",
                            opCtx.Invoice.Id
                        );
                        throw new CashuPaymentException(
                            $"There was a problem processing your request. Please contact the merchant with corresponding invoice Id: {opCtx.Invoice.Id}"
                        );
                }
            }
            catch (HttpRequestException)
            {
                ftx.Details = "Network error during melt retry; mint unreachable";
                logs.PayServer.LogDebug(
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
                logs.PayServer.LogDebug(
                    "(Cashu) ValidateLightningInvoicePaid threw for invoice {InvoiceId}: {Error}. Saving ftx for poller.",
                    opCtx.Invoice.Id, ex.Message
                );
                lnInvPaid = false;
            }

            if (lnInvPaid)
            {
                await RegisterCashuPayment(opCtx, meltQuoteResponse.Invoice?.Amount ?? LightMoney.Zero);

                logs.PayServer.LogDebug(
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
                // LN not yet paid (or unknown). Payment wasn't registered — the customer must
                // see failure so the merchant isn't left thinking the invoice settled.
                // The poller will finalize if the LN payment eventually lands.
                throw new CashuPaymentException(
                    $"There was a problem processing your request. Please contact the merchant with corresponding invoice Id: {opCtx.Invoice.Id}"
                );
            }
        }
        else
        {
            logs.PayServer.LogDebug("(Cashu) Unexpected melt error: {Error}", meltResponse.Error?.Message);
            throw new MintOperationException("Melt failed unexpectedly.", meltResponse.Error!);
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
            logs.PayServer.LogError(
                "(Cashu) Can't determine payment amount for failed tx {Id}: InputAmount={InputAmount} currency={Currency}. Manual registration required.",
                ftx.Id, ftx.InputAmount, invoice.Currency
            );
            return FtxPaymentRegistrationResult.UnresolvableAmount;
        }

        await RegisterCashuPayment(invoice, paymentAmount, BuildPaymentIdForFtx(ftx));
        return FtxPaymentRegistrationResult.Registered;
    }

    public Task RegisterCashuPayment(
        CashuOperationContext ctx,
        LightMoney value = null,
        bool markPaid = true
    ) => RegisterCashuPayment(
        ctx.Invoice,
        value ?? ctx.Value,
        BuildPaymentId(ctx.Invoice.Id, ctx.Token.Proofs),
        markPaid
    );

    /// <summary>
    /// Registers a Cashu payment for the invoice. Idempotent: identified by deterministic paymentId
    /// so retries, parallel callers, or partial-failure recoveries never double-count.
    /// </summary>
    public async Task RegisterCashuPayment(
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

    private static readonly JsonSerializerOptions SecretJsonOptions = new()
    {
        Converters = { new SecretJsonConverter() }
    };

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

    public async Task<PollResult> PollFailedMelt(
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

    public async Task<PollResult> PollFailedSwap(
        FailedTransaction ftx,
        StoreData storeData,
        CancellationToken cts = default
    )
    {
        if (ftx.OperationType != OperationType.Swap)
        {
            throw new InvalidOperationException($"Unexpected operation type: {ftx.OperationType}");
        }

        var wallet = await statefulWalletFactory.CreateAsync(ftx.StoreId, ftx.MintUrl, ftx.Unit);
        try
        {
            // Check if token is spent - if not, swap failed for 100%
            var tokenState = await wallet.CheckTokenState(ftx.InputProofs.ToList());
            if (tokenState == StateResponseItem.TokenState.UNSPENT)
            {
                return new PollResult() { State = CashuPaymentState.Failed };
            }

            //try to restore proofs
            var response = await wallet.RestoreProofsFromInputs(
                ftx.OutputData.Select(o => o.BlindedMessage).ToArray(),
                cts
            );
            if (response.Signatures.Length == ftx.OutputData.Count)
            {
                var firstSignature = response.Signatures.FirstOrDefault();
                if (firstSignature == null)
                {
                    return new PollResult { State = CashuPaymentState.Failed };
                }
                var keysetId = firstSignature.Id;
                var keys = await wallet.GetKeys(keysetId);
                if (keys == null)
                    return new PollResult { State = CashuPaymentState.Pending };
                var proofs = DotNut.Abstractions.Utils.ConstructProofsFromPromises(
                    response.Signatures.ToList(),
                    ftx.OutputData,
                    keys
                );
                return new PollResult()
                {
                    ResultProofs = proofs,
                    State = CashuPaymentState.Success,
                };
            }

            return new PollResult()
            {
                State = CashuPaymentState.Failed,
                Error = new CashuPluginException("Swap inputs and outputs aren't balanced!"),
            };
        }
        catch (HttpRequestException ex)
        {
            return new PollResult { State = CashuPaymentState.Pending, Error = ex };
        }
    }

    public class PollResult
    {
        public bool Success => State == CashuPaymentState.Success;
        public CashuPaymentState State { get; set; }
        public List<Proof>? ResultProofs { get; set; }
        public Exception? Error { get; set; }
    }
}
