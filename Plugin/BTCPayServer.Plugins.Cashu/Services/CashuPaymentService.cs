using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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


public class CashuPaymentService(
    StoreRepository storeRepository,
    InvoiceRepository invoiceRepository,
    CashuPaymentMethodHandler handler,
    PaymentMethodHandlerDictionary handlers,
    StatefulWalletFactory statefulWalletFactory,
    CashuMeltHandler meltHandler,
    CashuSwapHandler swapHandler,
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
            await swapHandler.ExecuteAsync(ctx, cancellationToken);
        }
        else if (melt)
        {
            await meltHandler.ExecuteAsync(ctx, cancellationToken);
        }
        else
        {
            throw new UntrustedMintException(ctx.Token.Mint);
        }
    }
    
}
