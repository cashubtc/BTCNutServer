using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.enums;
using BTCPayServer.Plugins.Cashu.Data.Models;
using BTCPayServer.Plugins.Cashu.Errors;
using BTCPayServer.Plugins.Cashu.Wallets;
using DotNut;
using DotNut.Api;
using DotNut.ApiModels;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Cashu.Services;

public class CashuSwapHandler(
    StatefulWalletFactory statefulWalletFactory,
    MintManager mintManager,
    CashuPaymentRegistrar cashuPaymentRegistrar,
    CashuDbContextFactory cashuDbContextFactory,
    ILogger<CashuSwapHandler> logs)
{
    public async Task ExecuteAsync(
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
            logs.LogDebug("(Cashu) Keyset ID conflict before swap: {Message}", ex.Message);
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
            logs.LogDebug("(Cashu) Keyset fees exceed limit: {Fee}. Token wasn't spent.", keysetFee);
            throw new FeesTooHighException($"Keyset fees ({keysetFee}) exceed the configured limit.");
        }

        logs.LogDebug(
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
                        var pollResult = await PollFailed(ftx, cts);

                        if (!pollResult.Success)
                        {
                            ftx.RetryCount += 1;
                            ftx.LastRetried = DateTimeOffset.UtcNow;
                            await using (var db = cashuDbContextFactory.CreateContext())
                            {
                                await db.FailedTransactions.AddAsync(ftx, cts);
                                await db.SaveChangesAsync(cts);
                            }
                            logs.LogDebug(
                                "(Cashu) Transaction {InvoiceId} failed: broken connection with mint. Saved as failed transaction.",
                                ctx.Invoice.Id
                            );
                            // surface failure to the caller — without this the UI reports success
                            // while the merchant's invoice remains unsettled.
                            throw new CashuPaymentException(
                                $"There was a problem processing your request. Please contact the merchant with corresponding invoice Id: {ctx.Invoice.Id}"
                            );
                        }

                        await cashuPaymentRegistrar.AddProofsToDb(
                            pollResult.ResultProofs!,
                            ftx.StoreId,
                            ftx.MintUrl,
                            ProofState.Available
                        );
                        await cashuPaymentRegistrar.Register(ctx);
                        return;
                    }
                default:
                    logs.LogDebug("(Cashu) Swap failed: {Error}", swapResult.Error?.Message);
                    throw new MintOperationException("Swap failed.", swapResult.Error!);
            }
        }

        var returnedAmount = swapResult.ResultProofs!.Select(p => p.Amount).Sum();
        logs.LogDebug("(Cashu) Swap success. {Amount} {Unit} received.", returnedAmount, ctx.Token.Unit);
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

            logs.LogDebug(
                "(Cashu) Mint returned less signatures than requested for transaction {InvoiceId}. Saved as failed transaction for recovery.",
                ctx.Invoice.Id
            );
            //TODO: Pay partially or retry to recover missing proofs
        }
        await cashuPaymentRegistrar.Register(ctx);
    }

    public async Task<PollResult> PollFailed(
        FailedTransaction ftx,
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
}