#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.enums;
using BTCPayServer.Plugins.Cashu.Data.Models;
using BTCPayServer.Plugins.Cashu.PaymentHandlers;
using BTCPayServer.Plugins.Cashu.ViewModels;
using BTCPayServer.Services.Invoices;
using DotNut;
using DotNut.Abstractions;
using DotNut.ApiModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Cashu.Controllers;

[Route("stores")]
[Authorize(
    Policy = Policies.CanModifyStoreSettings,
    AuthenticationSchemes = AuthenticationSchemes.Cookie
)]
public class UICashuWalletController : Controller
{
    public UICashuWalletController(
        InvoiceRepository invoiceRepository,
        PaymentMethodHandlerDictionary handlers,
        CashuPaymentService cashuPaymentService,
        CashuDbContextFactory cashuDbContextFactory,
        ILogger<UICashuWalletController> logger
    )
    {
        _invoiceRepository = invoiceRepository;
        _handlers = handlers;
        _cashuPaymentService = cashuPaymentService;
        _cashuDbContextFactory = cashuDbContextFactory;
        _logger = logger;
    }

    private StoreData StoreData => HttpContext.GetStoreData();

    private readonly InvoiceRepository _invoiceRepository;
    private readonly PaymentMethodHandlerDictionary _handlers;
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
            return RedirectToAction(
                "GettingStarted",
                "UICashuOnboarding",
                new { storeId = StoreData.Id }
            );
            ;
        }

        var mints = await db.Mints.Select(m => m.Url).ToListAsync();
        var proofsWithUnits = new List<(string Mint, string Unit, ulong Amount)>();

        var unavailableMints = new List<string>();

        foreach (var mint in mints)
        {
            try
            {
                using var cashuHttpClient = CashuUtils.GetCashuHttpClient(mint);
                var keysets = await cashuHttpClient.GetKeysets();

                var localProofs = await db
                    .Proofs.Where(p =>
                        keysets.Keysets.Select(k => k.Id).Contains(p.Id)
                        && p.StoreId == StoreData.Id
                        && p.Status == ProofState.Available
                    )
                    .ToListAsync();

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
                _logger.LogError(
                    $"[Cashu Wallet] Could not load mint {mint}, Exception: {ex.Message}"
                );
                unavailableMints.Add(mint);
            }
        }

        var groupedProofs = proofsWithUnits
            .GroupBy(p => new { p.Mint, p.Unit })
            .Select(group => new
            {
                group.Key.Mint,
                group.Key.Unit,
                Amount = group.Select(x => x.Amount).Sum(),
            })
            .OrderByDescending(x => x.Amount)
            .Select(x => (x.Mint, x.Unit, x.Amount))
            .ToList();

        var exportedTokens = db
            .ExportedTokens.Where(et => et.StoreId == StoreData.Id)
            .OrderByDescending(et => et.CreatedAt)
            .ToList();

        if (unavailableMints.Any())
        {
            TempData[WellKnownTempData.ErrorMessage] =
                $"Couldn't load {unavailableMints.Count} mints: {String.Join(", ", unavailableMints)}";
        }
        var viewModel = new CashuWalletViewModel
        {
            AvaibleBalances = groupedProofs,
            ExportedTokens = exportedTokens,
        };

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
        if (string.IsNullOrWhiteSpace(mintUrl) || string.IsNullOrWhiteSpace(unit))
        {
            TempData[WellKnownTempData.ErrorMessage] = "Invalid mint or unit provided!";
            return RedirectToAction("CashuWallet", new { storeId = StoreData.Id });
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
            return RedirectToAction("CashuWallet", new { storeId = StoreData.Id });
        }

        var selectedProofs = await db
            .Proofs.Where(p =>
                p.StoreId == StoreData.Id
                && keysets.Select(k => k.Id).Contains(p.Id)
                && p.Status == ProofState.Available
            )
            .ToListAsync();

        var createdToken = new CashuToken()
        {
            Tokens =
            [
                new CashuToken.Token
                {
                    Mint = mintUrl,
                    Proofs = selectedProofs.Select(p => p.ToDotNutProof()).ToList(),
                },
            ],
            Memo = "Cashu Token withdrawn from BTCNutServer",
            Unit = unit,
        };

        var tokenAmount = selectedProofs.Select(p => p.Amount).Sum();
        var serializedToken = createdToken.Encode();

        // mark proofs as exported and link to ExportedToken
        foreach (var proof in selectedProofs)
        {
            proof.Status = ProofState.Exported;
        }

        var exportedTokenEntity = new ExportedToken
        {
            SerializedToken = serializedToken,
            Amount = tokenAmount,
            Unit = unit,
            Mint = mintUrl,
            StoreId = StoreData.Id,
            IsUsed = false,
            Proofs = selectedProofs,
        };

        IActionResult result = RedirectToAction(
            "ExportedToken",
            new { tokenId = exportedTokenEntity.Id, storeId = StoreData.Id }
        );
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                db.ExportedTokens.Add(exportedTokenEntity);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                TempData[WellKnownTempData.ErrorMessage] = "Couldn't export token";
                result = RedirectToAction(nameof(CashuWallet), new { storeId = StoreData.Id });
            }
        });
        return result;
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

        var exportedToken = await db
            .ExportedTokens.Include(et => et.Proofs)
            .SingleOrDefaultAsync(e => e.Id == tokenId);

        if (exportedToken == null)
        {
            return BadRequest("Can't find token with provided GUID");
        }

        // in pre-release version there were no Proofs in exportedToken, and token had to be deserialized manually every time
        // this propably can be deleted in future.
        if (exportedToken.Proofs == null || exportedToken.Proofs.Count == 0)
        {
            var deserialized = CashuTokenHelper.Decode(exportedToken.SerializedToken, out _);
            var newProofs = StoredProof
                .FromBatch(
                    deserialized.Tokens.SelectMany(t => t.Proofs).ToList(),
                    storeId,
                    ProofState.Exported
                )
                .ToList();
            exportedToken.Proofs = new();
            exportedToken.Proofs.AddRange(newProofs);
            await db.SaveChangesAsync();
        }

        // check state if not already marked as used
        if (!exportedToken.IsUsed && exportedToken.Proofs.Count > 0)
        {
            try
            {
                var wallet = new StatefulWallet(exportedToken.Mint, exportedToken.Unit);
                var state = await wallet.CheckTokenState(exportedToken.Proofs);

                if (state == StateResponseItem.TokenState.SPENT)
                {
                    exportedToken.IsUsed = true;
                    foreach (var proof in exportedToken.Proofs)
                    {
                        proof.Status = ProofState.Spent;
                    }
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception)
            {
                // mint unreachable - will check next time
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
        var failedTransactions = await db
            .FailedTransactions.Where(ft => ft.StoreId == StoreData.Id)
            .ToListAsync();

        return View("Views/Cashu/FailedTransactions.cshtml", failedTransactions);
    }

    [HttpPost("{storeId}/cashu/failed-transactions/{failedTransactionId}")]
    public async Task<IActionResult> PostFailedTransaction(string storeId, Guid failedTransactionId)
    {
        await using var db = _cashuDbContextFactory.CreateContext();
        var failedTransaction = db.FailedTransactions.SingleOrDefault(t =>
            t.Id == failedTransactionId
        );

        if (failedTransaction == null)
        {
            TempData[WellKnownTempData.ErrorMessage] =
                "Can't get failed transaction with provided GUID!";
            return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id });
        }

        if (failedTransaction.StoreId != StoreData.Id)
        {
            TempData[WellKnownTempData.ErrorMessage] =
                "Chosen failed transaction doesn't belong to this store!";
            return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id });
        }

        var handler = _handlers[CashuPlugin.CashuPmid];

        if (handler is not CashuPaymentMethodHandler cashuHandler)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Couldn't obtain CashuPaymentHandler";
            return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id });
        }

        var invoice = await _invoiceRepository.GetInvoice(failedTransaction.InvoiceId);

        if (invoice is null)
        {
            TempData[WellKnownTempData.ErrorMessage] =
                "Couldn't find invoice with provided GUID in this store.";
            return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id });
        }

        CashuPaymentService.PollResult pollResult;

        try
        {
            if (failedTransaction.OperationType == OperationType.Melt)
            {
                pollResult = await _cashuPaymentService.PollFailedMelt(
                    failedTransaction,
                    StoreData,
                    cashuHandler
                );
            }
            else
            {
                pollResult = await _cashuPaymentService.PollFailedSwap(
                    failedTransaction,
                    StoreData
                );
            }
        }
        catch (Exception ex)
        {
            TempData[WellKnownTempData.ErrorMessage] =
                "Couldn't poll failed transaction: " + ex.Message;
            return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id });
        }

        if (pollResult == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Polling failed. Received no response.";
            return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id });
        }

        if (!pollResult.Success)
        {
            TempData[WellKnownTempData.ErrorMessage] =
                $"Transaction state: {pollResult.State}. {(pollResult.Error == null ? "" : pollResult.Error.Message)}";
            return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id });
        }

        await _cashuPaymentService.AddProofsToDb(
            pollResult.ResultProofs,
            StoreData.Id,
            failedTransaction.MintUrl,
            ProofState.Available
        );
        decimal singleUnitPrice;
        try
        {
            singleUnitPrice = await CashuUtils.GetTokenSatRate(
                failedTransaction.MintUrl,
                failedTransaction.Unit,
                cashuHandler.Network.NBitcoinNetwork
            );
        }
        catch (Exception)
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Couldn't fetch token/satoshi rate";
            return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id });
        }

        // calculate payment amount
        ulong inputAmount = failedTransaction.InputAmount;

        // old FailedTransactions have InputAmount = 0 and InputProofsJson = NULL
        // this happened because the migration added these columns with default values
        // the actual input proofs were deleted from the Proofs table (they were customer's proofs, not wallet proofs)
        // for recovery, we use the invoice amount as approximation
        if (inputAmount == 0)
        {
            // use invoice amount divided by single unit price as approximation
            var invoiceAmountSats = invoice.Price;
            inputAmount = (ulong)(invoiceAmountSats / singleUnitPrice);

            // this is best-effort recovery for old failed transactions
            // the actual input amount is lost, but invoice amount should be close enough
        }

        await _cashuPaymentService.RegisterCashuPayment(
            invoice,
            cashuHandler,
            Money.Satoshis(inputAmount * singleUnitPrice)
        );
        db.FailedTransactions.Remove(failedTransaction);
        await db.SaveChangesAsync();
        TempData[WellKnownTempData.SuccessMessage] =
            $"Transaction retrieved successfully. Marked as paid.";
        return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id });
    }

    [HttpGet("~/cashu/mint-info")]
    public async Task<IActionResult> GetMintInfo(string mintUrl)
    {
        if (
            !mintUrl.StartsWith("http") && !mintUrl.StartsWith("https")
            || !Uri.TryCreate(mintUrl, UriKind.Absolute, out var uri)
        )
        {
            return BadRequest("Invalid mint url provided!");
        }

        try
        {
            var info = await Wallet.Create().WithMint(uri).GetInfo();

            var dto = new
            {
                name = info.Name,
                description = info.Description,
                description_long = info.DescriptionLong,
                contact = new
                {
                    email = info.Contact?.FirstOrDefault(i => i?.Method == "email", null)?.Info,
                    twitter = info.Contact?.FirstOrDefault(i => i?.Method == "twitter", null)?.Info,
                    nostr = info.Contact?.FirstOrDefault(i => i?.Method == "nostr", null)?.Info,
                },
                nuts = info.Nuts?.Keys,
                currency = info.IsSupportedMintMelt(4).Methods.Select(m => m.Unit).Distinct(),
                version = info.Version,
                url = mintUrl,
            };

            return Ok(dto);
        }
        catch
        {
            return NotFound("Failed to fetch mint info");
        }
    }
}
