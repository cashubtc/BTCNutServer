#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.PaymentHandlers;
using BTCPayServer.Plugins.Cashu.ViewModels;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BTCPayServer.Plugins.Cashu.Data.Models;
using DotNut;
using DotNut.Abstractions;
using DotNut.ApiModels;
using Microsoft.Extensions.Logging;
using NBitcoin;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Cashu.Controllers;

[Route("stores")]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UICashuWalletController : Controller
{
    public UICashuWalletController(
        InvoiceRepository invoiceRepository,
        PaymentMethodHandlerDictionary handlers,
        CashuStatusProvider cashuStatusProvider,
        CashuPaymentService cashuPaymentService,
        CashuDbContextFactory cashuDbContextFactory,
        ILogger<UICashuWalletController> logger)
    {
        _invoiceRepository = invoiceRepository;
        _handlers = handlers;
        _cashuStatusProvider = cashuStatusProvider;
        _cashuPaymentService = cashuPaymentService;
        _cashuDbContextFactory = cashuDbContextFactory;
        _logger = logger;
    }

    private StoreData StoreData => HttpContext.GetStoreData();

    private readonly InvoiceRepository _invoiceRepository;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly CashuStatusProvider _cashuStatusProvider;
    private readonly CashuPaymentService _cashuPaymentService;
    private readonly CashuDbContextFactory _cashuDbContextFactory;
    private readonly ILogger<UICashuWalletController> _logger;
    

    /// <summary>
    /// Api route for fetching current store Cashu Wallet view - All stored proofs grouped by mint and unit which can be exported.
    /// </summary>
    /// <returns></returns>
    [HttpGet("{storeId}/cashu/wallet")]
    public async Task<IActionResult> CashuWallet(string storeId)
    {
        await using var db = _cashuDbContextFactory.CreateContext();
        if (!db.CashuWalletConfig.Any(cwc => cwc.StoreId == StoreData.Id))
        {
            return RedirectToAction("GettingStarted", "UICashuOnboarding", new { storeId = StoreData.Id });;
        }


        var mints = await db.Mints.Select(m => m.Url).ToListAsync();
        var proofsWithUnits = new List<(string Mint, string Unit, ulong Amount)>();

        var unavailableMints = new List<string>();

        foreach (var mint in mints)
        {
            try
            {
                var cashuHttpClient = CashuUtils.GetCashuHttpClient(mint);
                var keysets = await cashuHttpClient.GetKeysets();

               var localProofs = await db.Proofs
                   .Where(p => keysets.Keysets.Select(k => k.Id).Contains(p.Id) &&
                               p.StoreId == StoreData.Id &&
                                 !db.FailedTransactions.Any(ft => ft.UsedProofs.Contains(p)
                                     )).ToListAsync();

                foreach (var proof in localProofs)
                {
                    var matchingKeyset = keysets.Keysets.FirstOrDefault(k => k.Id == proof.Id);
                    if (matchingKeyset != null)
                    {
                        proofsWithUnits.Add((Mint: mint, matchingKeyset.Unit, proof.Amount));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Cashu Wallet] Could not load mint {mint}, Exception: {ex.Message}");
                unavailableMints.Add(mint);
            }
        }

        var groupedProofs = proofsWithUnits
            .GroupBy(p => new { p.Mint, p.Unit })
            .Select(group => new
                {
                 group.Key.Mint,
                 group.Key.Unit,
                 Amount = group.Select(x => x.Amount).Sum()
                }
            )
            .OrderByDescending(x => x.Amount)
            .Select(x => (x.Mint, x.Unit, x.Amount))
            .ToList();

        var exportedTokens = db.ExportedTokens.Where(et=>et.StoreId == StoreData.Id).ToList();
        if (unavailableMints.Any())
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Couldn't load {unavailableMints.Count} mints: {String.Join(", ", unavailableMints)}";
        }
        var viewModel = new CashuWalletViewModel {AvaibleBalances = groupedProofs, ExportedTokens = exportedTokens};

        return View("Views/Cashu/CashuWallet.cshtml", viewModel);
    }

    /// <summary>
    /// Api route for exporting stored balance for chosen mint and unit
    /// </summary>
    /// <param name="storeId">Store ID</param>
    /// <param name="mintUrl">Chosen mint url, form which proofs we want to export</param>
    /// <param name="unit">Chosen unit of token</param>
    [HttpPost("{storeId}/cashu/export-mint-balance")]
    public async Task<IActionResult> ExportMintBalance(string storeId, string mintUrl, string unit)
    {
        if (string.IsNullOrWhiteSpace(mintUrl)|| string.IsNullOrWhiteSpace(unit))
        {
            TempData[WellKnownTempData.ErrorMessage] = "Invalid mint or unit provided!";
            return RedirectToAction("CashuWallet", new { storeId = StoreData.Id});
        }

        await using var db = _cashuDbContextFactory.CreateContext();
        List<GetKeysetsResponse.KeysetItemResponse> keysets;
        try
        {
            var cashuWallet = new StatefulWallet(mintUrl, unit);
            keysets = await cashuWallet.GetKeysets();
            if (keysets == null || keysets.Count == 0)
            {
                throw new Exception("No keysets were found.");
            }
        }
        catch (Exception)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Couldn't get keysets!";
            return RedirectToAction("CashuWallet", new { storeId = StoreData.Id});
        }

        var selectedProofs = db.Proofs.Where(p=>
            p.StoreId == StoreData.Id
            && keysets.Select(k => k.Id).Contains(p.Id)
            //ensure that proof is free and spendable (yeah!)
            && !db.FailedTransactions.Any(ft => ft.UsedProofs.Contains(p))
            ).ToList();

        var createdToken = new CashuToken()
        {
            Tokens =
            [
                new CashuToken.Token
                {
                    Mint = mintUrl,
                    Proofs = selectedProofs.Select(p => p.ToDotNutProof()).ToList(),
                }
            ],
            Memo = "Cashu Token withdrawn from BTCNutServer",
            Unit = unit
        };

        var tokenAmount = selectedProofs.Select(p => p.Amount).Sum();
        var serializedToken = createdToken.Encode();

        var proofsToRemove = await db.Proofs
            .Where(p => p.StoreId == StoreData.Id &&
                        keysets.Select(k => k.Id).Contains(p.Id))
            .ToListAsync();

        var exportedTokenEntity = new ExportedToken
        {
            SerializedToken = serializedToken,
            Amount = tokenAmount,
            Unit = unit,
            Mint = mintUrl,
            StoreId = StoreData.Id,
            IsUsed = false,
        };
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                db.Proofs.RemoveRange(proofsToRemove);
                db.ExportedTokens.Add(exportedTokenEntity);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                ViewData[WellKnownTempData.ErrorMessage] = "Couldn't export";
                RedirectToAction("CashuWallet", new { storeId = StoreData.Id });
            }
        });
        return RedirectToAction("ExportedToken", new { tokenId = exportedTokenEntity.Id });
    }

    /// <summary>
    /// Api route for fetching exported token data
    /// </summary>
    /// <param name="storeId">Store ID</param>
    /// <param name="tokenId">Stored Token GUID</param>
    [HttpGet("{storeId}/cashu/token/{tokenId}")]
    public async Task<IActionResult> ExportedToken(string storeId, Guid tokenId)
    {

        await using var db = _cashuDbContextFactory.CreateContext();

        var exportedToken = db.ExportedTokens.SingleOrDefault(e => e.Id == tokenId);
        if (exportedToken == null)
        {
            return BadRequest("Can't find token with provided GUID");
        }

        //todo move this logic into main wallet screen. maybe do a "state check" button? 
        if (!exportedToken.IsUsed)
        {
            try
            {
                var wallet = new StatefulWallet(exportedToken.Mint, exportedToken.Unit);
                var proofs = CashuTokenHelper.Decode(exportedToken.SerializedToken, out _)
                    .Tokens.SelectMany(t => t.Proofs)
                    .Distinct()
                    .ToList();
                var state = await wallet.CheckTokenState(proofs);
                if (state == StateResponseItem.TokenState.SPENT)
                {
                    exportedToken.IsUsed = true;
                    db.ExportedTokens.Update(exportedToken);
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception)
            {
                //honestly, there's nothing to do. maybe it'll work next time
            }
        }

        var model = new ExportedTokenViewModel()
        {
            Amount = exportedToken.Amount,
            Unit = exportedToken.Unit,
            MintAddress = exportedToken.Mint,
            Token = exportedToken.SerializedToken,
        };

        return View("Views/Cashu/ExportedToken.cshtml", model);
    }

    /// <summary>
    /// Api route for fetching failed transactions list
    /// </summary>
    /// <returns></returns>
    [HttpGet("{storeId}/cashu/failed-transactions")]
    public async Task<IActionResult> FailedTransactions(string storeId)
    {
        await using var db = _cashuDbContextFactory.CreateContext();
        //fetch recently failed transactions
        var failedTransactions = db.FailedTransactions
            .Where(ft => ft.StoreId == StoreData.Id)
            .Include(ft=>ft.UsedProofs)
            .ToList();

        return View("Views/Cashu/FailedTransactions.cshtml", failedTransactions);
    }


    /// <summary>
    /// Api route for checking failed transaction state.
    /// </summary>
    /// <param name="storeId">Store ID</param>
    /// <param name="failedTransactionId"></param>
    /// <returns></returns>
    [HttpPost("{storeId}/cashu/failed-transactions/{failedTransactionId}")]
    public async Task<IActionResult> PostFailedTransaction(string storeId, Guid failedTransactionId)
    {
        await using var db = _cashuDbContextFactory.CreateContext();
        var failedTransaction = db.FailedTransactions
            .Include(failedTransaction => failedTransaction.UsedProofs)
            .SingleOrDefault(t => t.Id == failedTransactionId);

        if (failedTransaction == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Can't get failed transaction with provided GUID!";
            return RedirectToAction("FailedTransactions",  new { storeId = StoreData.Id});
        }

        if (failedTransaction.StoreId != StoreData.Id)
        {
            TempData[WellKnownTempData.ErrorMessage] ="Chosen failed transaction doesn't belong to this store!";
            return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id});
        }

        var handler = _handlers[CashuPlugin.CashuPmid];

        if (handler is not CashuPaymentMethodHandler cashuHandler)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Couldn't obtain CashuPaymentHandler";
            return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id});
        }

        var invoice = await _invoiceRepository.GetInvoice(failedTransaction.InvoiceId);

        if (invoice is null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Couldn't find invoice with provided GUID in this store.";
            return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id});
        }

        CashuPaymentService.PollResult pollResult;

        try
        {
            if (failedTransaction.OperationType == OperationType.Melt)
            {
                pollResult = await _cashuPaymentService.PollFailedMelt(failedTransaction, StoreData, cashuHandler);
            }
            else
            {
                pollResult = await _cashuPaymentService.PollFailedSwap(failedTransaction, StoreData);
            }
        }
        catch (Exception ex)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Couldn't poll failed transaction: " + ex.Message;
            return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id});

        }

        if (pollResult == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Polling failed. Received no response.";
            return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id});
        }

        if (!pollResult.Success)
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Transaction state: {pollResult.State}. {(pollResult.Error == null ? "" : pollResult.Error.Message)}";
            return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id});
        }

        await _cashuPaymentService.AddProofsToDb(pollResult.ResultProofs, StoreData.Id, failedTransaction.MintUrl);
        decimal singleUnitPrice;
        try
        {
            singleUnitPrice = await CashuUtils.GetTokenSatRate(failedTransaction.MintUrl, failedTransaction.Unit,
                cashuHandler.Network.NBitcoinNetwork);

        }
        catch (Exception)
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Couldn't fetch token/satoshi rate";
            return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id});
        }

        var summedProofs = failedTransaction.UsedProofs.Select(p=>p.Amount).Sum();
        await _cashuPaymentService.RegisterCashuPayment(invoice, cashuHandler, Money.Satoshis(summedProofs*singleUnitPrice));
        db.FailedTransactions.Remove(failedTransaction);
        await db.SaveChangesAsync();
        TempData[WellKnownTempData.SuccessMessage] = $"Transaction retrieved successfully. Marked as paid.";
        return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id});
    }

    [HttpGet("~/cashu/mint-info")]
    public async Task<IActionResult> GetMintInfo(string mintUrl)
    {
        if (!mintUrl.StartsWith("http") && !mintUrl.StartsWith("https") 
            || !Uri.TryCreate(mintUrl, UriKind.Absolute, out var uri))
        {
            return BadRequest("Invalid mint url provided!");
        }

        try
        {
            var info = await Wallet
                .Create()
                .WithMint(uri)
                .GetInfo();
            
            var dto = new
            {
                name = info.Name,
                description = info.Description,
                description_long = info.DescriptionLong,
                contact = new
                {
                    email = info.Contact?.FirstOrDefault(i=>i?.Method == "email", null)?.Info,
                    twitter = info.Contact?.FirstOrDefault(i=>i?.Method == "twitter", null)?.Info,
                    nostr = info.Contact?.FirstOrDefault(i=>i?.Method == "nostr", null)?.Info,
                },
                nuts = info.Nuts?.Keys,
                currency = info.IsSupportedMintMelt(4).Methods.Select(m=>m.Unit).Distinct(),
                version = info.Version,
                url = mintUrl
            };

            
            return Ok(dto);
        }
        catch
        {
            return NotFound("Failed to fetch mint info");
        }
    }
}
