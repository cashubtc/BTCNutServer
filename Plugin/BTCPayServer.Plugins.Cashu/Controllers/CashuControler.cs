using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.enums;
using BTCPayServer.Plugins.Cashu.Errors;
using BTCPayServer.Plugins.Cashu.PaymentHandlers;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.ViewModels;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BTCPayServer.Plugins.Cashu.Data.Models;
using DotNut;
using DotNut.ApiModels;
using Microsoft.AspNetCore.Cors;
using NBitcoin;
using Newtonsoft.Json.Linq;
using DesignTimeDbContextFactory = BTCPayServer.Data.DesignTimeDbContextFactory;
using PubKey = DotNut.PubKey;
using StoreData = BTCPayServer.Data.StoreData;
using DNBIP39 = DotNut.NBitcoin.BIP39;
namespace BTCPayServer.Plugins.Cashu.Controllers;

// todo this entire controler is too fat. it should be separated into few smaller components
// also this route is too generic for everything. should separate wallet from onboarding,
// the same applies for payment stuff
[Route("stores/{storeId}/cashu")]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class CashuController: Controller
{
    public CashuController(InvoiceRepository invoiceRepository,
        StoreRepository storeRepository,
        PaymentMethodHandlerDictionary handlers,
        CashuStatusProvider cashuStatusProvider,
        CashuPaymentService cashuPaymentService,
        CashuDbContextFactory cashuDbContextFactory,
        RestoreService restoreService)
    {
        _invoiceRepository = invoiceRepository;
        _storeRepository = storeRepository;
        _cashuPaymentService = cashuPaymentService;
        _cashuDbContextFactory = cashuDbContextFactory;
        _cashuStatusProvider = cashuStatusProvider;
        _handlers = handlers;
        _restoreService = restoreService;
    }
    private StoreData StoreData => HttpContext.GetStoreData();
    
    private readonly InvoiceRepository _invoiceRepository;
    private readonly StoreRepository _storeRepository;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly CashuStatusProvider _cashuStatusProvider;
    private readonly CashuPaymentService _cashuPaymentService;
    private readonly CashuDbContextFactory _cashuDbContextFactory;
    private readonly RestoreService _restoreService;
    
    /*
     * ================================
     *      Onboarding process
     * ================================
     */
    
    [HttpGet("getting-started")]
    public async Task<IActionResult> GettingStarted()
    {
        var db = _cashuDbContextFactory.CreateContext();
        if (StoreData == null || db.CashuWalletConfig.Any(cwc => cwc.StoreId == StoreData.Id))
        {
            return NotFound();
        }

        var model = new GettingStartedViewModel()
        {
            StoreId = StoreData.Id,
        };

        return View("Onboarding/GettingStarted", model);
    }

    [HttpGet("restore-wallet")]
    public async Task<IActionResult> RestoreFromMnemonic()
    {
        return View("Onboarding/RestoreFromMnemonic");
    }
    
    [HttpPost("restore-wallet")]
    public async Task<IActionResult> Restore(WalletRestoreViewModel model)
    {
        if (StoreData == null)
        {
            return NotFound();
        }
        var jobId = _restoreService.QueueRestore(StoreData.Id, model.MintUrls, model.Mnemonic);
        return View("Onboarding/RestoreProgress", jobId);
    }

    [HttpGet("/cashu/restore-status")]
    public async Task<IActionResult> RestoreStatus(string jobId)
    {
        var status = _restoreService.GetRestoreStatus(jobId);
        return Ok(status);
    }
    
    [HttpGet("create-mnemonic")]
    public async Task<IActionResult> CreateMnemonic()
    {
        var db = _cashuDbContextFactory.CreateContext();
        if (StoreData == null)
        {
            return NotFound();
        }
        // in case of user coming back... 
        var existingMnemonic = await db.CashuWalletConfig.SingleOrDefaultAsync(cwc => cwc.StoreId == StoreData.Id);
        if (existingMnemonic != null)
        {
            var existingModel = new Models.StoreViewModels.RecoverySeedBackupViewModel()
            {
                CryptoCode = "CASHU",
                IsStored = true,
                Mnemonic = existingMnemonic.WalletMnemonic.ToString(),
                RequireConfirm = true,
                ReturnUrl = Url.Action(nameof(ConfirmMnemonic), new { storeId = StoreData.Id })
            };
            return View("Views/Cashu/Onboarding/CreateMnemonic",existingModel);
        }
        
        var mnemonic = new DNBIP39.Mnemonic(DNBIP39.Wordlist.English, DNBIP39.WordCount.Twelve);
        var walletConfig = new CashuWalletConfig
        {
            StoreId = StoreData.Id,
            WalletMnemonic = mnemonic,
        };
        db.CashuWalletConfig.Add(walletConfig);
        await db.SaveChangesAsync();

        var model = new Models.StoreViewModels.RecoverySeedBackupViewModel()
        {
            CryptoCode = "CASHU",
            IsStored = true,
            Mnemonic = walletConfig.WalletMnemonic.ToString(),
            RequireConfirm = true,
            ReturnUrl = Url.Action(nameof(ConfirmMnemonic), new { storeId = StoreData.Id })
        };
        return View("Onboarding/CreateMnemonic",model);
    }
    
    [HttpGet("confirm-mnemonic")]
    public async Task<IActionResult> ConfirmMnemonic()
    {
        var db = _cashuDbContextFactory.CreateContext();
        if (StoreData == null)
        {
            return NotFound();
        }
        var randomMnemonic =  new DNBIP39.Mnemonic(DNBIP39.Wordlist.English, DNBIP39.WordCount.Twelve);
        var userMnemonic = await db.CashuWalletConfig.SingleOrDefaultAsync(cwc => cwc.StoreId == StoreData.Id);
        if (userMnemonic == null)
        {
            return NotFound();
        }
        
        var rand = new Random();
        var randomList = new List<string>();
        randomList.AddRange(userMnemonic.WalletMnemonic.Words.Take(4));
        randomList.AddRange(randomMnemonic.Words.Take(8));
        randomList = randomList.OrderBy(_ => rand.Next()).ToList();
    
        var model = new ConfirmMnemonicViewModel
        {
            Mnemonic = userMnemonic.WalletMnemonic.ToString(),
            Words = randomList,
            ViewMnemonicUrl =  Url.Action(nameof(ConfirmMnemonic))
        };
        
        return View("Onboarding/ConfirmMnemonic", model);
    }
    
    [HttpPost("confirm-mnemonic")]
    public async Task<IActionResult> ConfirmMnemonic(string fourWordChunk)
    {
        var db = _cashuDbContextFactory.CreateContext();
        var userMnemonic =await  db.CashuWalletConfig.SingleOrDefaultAsync(cwc => cwc.StoreId == StoreData.Id);
        var validChunk = string.Join("", userMnemonic.WalletMnemonic.Words.Take(4));
        if (!Equals(validChunk, fourWordChunk))
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Invalid words chosen. Try again";
            return RedirectToAction(nameof(ConfirmMnemonic), new { storeId = StoreData.Id });
        }
    
        userMnemonic.Verified = true;
        await db.SaveChangesAsync();
        TempData[WellKnownTempData.SuccessMessage] = $"Wallet created and verified successfully!";
        var hasLightning = StoreData.IsLightningEnabled("BTC");
        if (!hasLightning)
        {
            return RedirectToAction(nameof(InitWithoutLightning),new {storeId = StoreData.Id});
        }
        return RedirectToAction(nameof(StoreConfig), new { storeId = StoreData.Id });
    }

    [HttpGet("init-without-lightning")]
    public async Task<IActionResult> InitWithoutLightning()
    {
        var model = new CashuInitWithoutLightningViewModel
        {
            TrustedMintsUrls = string.Empty,
            PaymentAcceptanceModel = CashuPaymentModel.TrustedMintsOnly,
            ReturnUrl =  Url.Action(nameof(StoreConfig), new { storeId = StoreData.Id })
        };
        return View("Views/Cashu/Onboarding/InitWithoutLightning", model);
    }
    
    /*
     * ==================================
     *  Store Configuration and Settings
     * ==================================
     */
    
    /// <summary>
    /// Api route for fetching current plugin configuration for this store
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> StoreConfig()
    {
        var cashuPaymentMethodConfig =
            StoreData.GetPaymentMethodConfig<CashuPaymentMethodConfig>(CashuPlugin.CashuPmid, _handlers);
        {
            await using var db = _cashuDbContextFactory.CreateContext();
            var config = db.CashuWalletConfig.FirstOrDefault(cwc => cwc.StoreId == StoreData.Id);
            if (config == null)
            {
                return RedirectToAction(nameof(GettingStarted), new { storeId = StoreData.Id });
            }

            if (!config.Verified)
            {
                return RedirectToAction(nameof(ConfirmMnemonic), new { storeId = StoreData.Id });
            }
        }
        
        CashuStoreViewModel model = new CashuStoreViewModel();
        model.HasLightningNodeConnected = StoreData.IsLightningEnabled("BTC");
        if (cashuPaymentMethodConfig == null)
        {
            model.Enabled = await _cashuStatusProvider.CashuEnabled(StoreData.Id);
            model.PaymentAcceptanceModel = CashuPaymentModel.TrustedMintsOnly;
            model.TrustedMintsUrls = "";
        }
        else
        {
            model.Enabled = await _cashuStatusProvider.CashuEnabled(StoreData.Id);
            model.PaymentAcceptanceModel = cashuPaymentMethodConfig.PaymentModel;
            model.TrustedMintsUrls = String.Join("\n", cashuPaymentMethodConfig.TrustedMintsUrls ?? [""]);
        }

        return View(model);
    }
    
    /// <summary>
    /// Api route for setting plugin configuration for this store
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> StoreConfig(CashuStoreViewModel viewModel)
    {
        var store = StoreData;
        var blob = StoreData.GetStoreBlob();
        viewModel.TrustedMintsUrls ??= "";

        //trimming trailing slash 
        var parsedTrustedMintsUrls = viewModel.TrustedMintsUrls
            .Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimEnd('/'))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        var lightningEnabled = StoreData.IsLightningEnabled("BTC");

        //If lighting isn't configured - don't allow user to set meltImmediately.
        var paymentMethodConfig = new CashuPaymentMethodConfig()
        {
            PaymentModel = lightningEnabled ? viewModel.PaymentAcceptanceModel : CashuPaymentModel.TrustedMintsOnly,
            TrustedMintsUrls = parsedTrustedMintsUrls,
        };

        blob.SetExcluded(CashuPlugin.CashuPmid, !viewModel.Enabled);

        StoreData.SetPaymentMethodConfig(_handlers[CashuPlugin.CashuPmid], paymentMethodConfig);
        store.SetStoreBlob(blob);
        await _storeRepository.UpdateStore(store);
        if (viewModel.PaymentAcceptanceModel == CashuPaymentModel.HoldWhenTrusted && !lightningEnabled)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Can't use this payment model. Lightning wallet is disabled.";
        }
        else
        {
            TempData[WellKnownTempData.SuccessMessage] = "Config Saved Successfully";
        }

        return RedirectToAction("StoreConfig", new { storeId = store.Id, CashuPlugin.CashuPmid });
    }

    [HttpGet("settings")]
    public async Task<IActionResult> Settings()
    {
        var storeConfig = StoreData.GetPaymentMethodConfig<CashuPaymentMethodConfig>(CashuPlugin.CashuPmid, _handlers);
        if (storeConfig == null)
        {
            return RedirectToAction(nameof(StoreConfig), new { storeId = StoreData.Id });
        }
        var feeConfig =
            storeConfig.FeeConfing ?? new CashuFeeConfig
            {
                CustomerFeeAdvance = 0,
                MaxLightningFee = 0,
                MaxKeysetFee = 0,
            };
        var model = new CashuSettingsViewModel
        {
            CustomerFeeAdvance = feeConfig.CustomerFeeAdvance,
            MaxLightningFee = feeConfig.MaxLightningFee,
            MaxKeysetFee = feeConfig.MaxKeysetFee,
        };

        return View("Views/Cashu/Settings/FeeSettings", model);
    }

    [HttpPost("settings")]
    public async Task<IActionResult> Settings(CashuSettingsViewModel viewModel)
    {
        var config = StoreData.GetPaymentMethodConfig<CashuPaymentMethodConfig>(CashuPlugin.CashuPmid, _handlers);
        if (config == null)
        {
            // todo propably at this point we should redirect to StoreInit or something like that
            return RedirectToAction(nameof(GettingStarted), new { storeId = StoreData.Id });
        }
        config.FeeConfing.CustomerFeeAdvance = viewModel.CustomerFeeAdvance;
        config.FeeConfing.MaxLightningFee = viewModel.MaxLightningFee;
        config.FeeConfing.MaxKeysetFee = viewModel.MaxKeysetFee;
        
        StoreData.SetPaymentMethodConfig(_handlers[CashuPlugin.CashuPmid], config);
        return View("Views/Cashu/Settings/FeeSettings", viewModel);
    }
    
     /// <summary> 
     /// Api route for checking failed transaction state. 
     /// </summary> 
     /// <param name="failedTransactionId"></param> 
     /// <returns></returns>
     [HttpPost("failed-transactionss")]
     public async Task<IActionResult> PostFailedTransaction(Guid failedTransactionId)
     {
         await using var db = _cashuDbContextFactory.CreateContext();
         var failedTransaction = db.FailedTransactions
             .Include(failedTransaction => failedTransaction.UsedProofs)
             .SingleOrDefault(t => t.Id == failedTransactionId);
         
         if (failedTransaction == null)
         {
             TempData[WellKnownTempData.ErrorMessage] = "Can't get failed transaction with provided GUID!";
             return RedirectToAction(nameof(FailedTransactions),  new { storeId = StoreData.Id});
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
    
    /*
     * ==================================
     *      Internal Cashu Wallet
     * ==================================
     */

    /// <summary>
    /// Api route for fetching current store Cashu Wallet view - All stored proofs grouped by mint and unit which can be exported.
    /// </summary>
    /// <returns></returns>
    [HttpGet("wallet")]
    public async Task<IActionResult> CashuWallet()
    {
        await using var db = _cashuDbContextFactory.CreateContext();
        if (!db.CashuWalletConfig.Any(cwc => cwc.StoreId == StoreData.Id))
        {
            return RedirectToAction(nameof(GettingStarted), new { storeId = StoreData.Id });;
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
            catch (Exception)
            {
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
        
        return View(viewModel);
    }
    
    /// <summary>
    /// Api route for exporting stored balance for chosen mint and unit
    /// </summary>
    /// <param name="mintUrl">Chosen mint url, form which proofs we want to export</param>
    /// <param name="unit">Chosen unit of token</param>
    [HttpPost("export-mint-balance")]
    public async Task<IActionResult> ExportMintBalance(string mintUrl, string unit)
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
            var cashuWallet = new CashuWallet(mintUrl, unit);
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
                RedirectToAction(nameof(CashuWallet), new { storeId = StoreData.Id });
            }
        });
        return RedirectToAction(nameof(ExportedToken), new { tokenId = exportedTokenEntity.Id });
    }

    /// <summary>
    /// Api route for fetching exported token data
    /// </summary>
    /// <param name="tokenId">Stored Token GUID</param>
    [HttpGet("/token")]
    public async Task<IActionResult> ExportedToken(Guid tokenId)
    {
        
        var db = _cashuDbContextFactory.CreateContext();
       
        var exportedToken = db.ExportedTokens.SingleOrDefault(e => e.Id == tokenId);
        if (exportedToken == null)
        {
            return BadRequest("Can't find token with provided GUID");
        }

        if (!exportedToken.IsUsed)
        { 
            try
            {
                var wallet = new CashuWallet(exportedToken.Mint, exportedToken.Unit);
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
        
        return View(model);
    }

    /// <summary>
    /// Api route for fetching failed transactions list
    /// </summary>
    /// <returns></returns>
    [HttpGet("failed-transactions")]
    public async Task<IActionResult> FailedTransactions()
    {
        await using var db = _cashuDbContextFactory.CreateContext();
        //fetch recently failed transactions 
        var failedTransactions = db.FailedTransactions
            .Where(ft => ft.StoreId == StoreData.Id)
            .Include(ft=>ft.UsedProofs)
            .ToList();
            
        return View(failedTransactions);
    }
    
    
    /*
     * ==================================
     *       Payment endpoints
     * ==================================
     */
    
    /// <summary>
    /// Api endpoint for Paying Invoice via Post Request, by sending all the data exclusively.
    /// </summary>
    /// <param name="token">V4 encoded Cashu Token</param>
    /// <param name="invoiceId"></param>
    /// <returns></returns>
    /// <exception cref="CashuPaymentException"></exception>
    [EnableCors(CorsPolicies.All)]
    [AllowAnonymous]
    [HttpPost("~/cashu/pay-invoice")]
    public async Task<IActionResult> PayByToken(string token, string invoiceId)
    {
        try
        {
            if (!CashuUtils.TryDecodeToken(token, out var decodedToken))
            {
                throw new CashuPaymentException("Invalid token");
            }
            await _cashuPaymentService.ProcessPaymentAsync(decodedToken, invoiceId);
        }
        catch (CashuPaymentException cex)
        {
            return BadRequest($"Payment Error: {cex.Message} ");
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }

        return Redirect(Url.ActionAbsolute(this.Request, nameof(UIInvoiceController.Checkout), "UIInvoice", new { invoiceId = invoiceId }).AbsoluteUri);
    }
    
    /// <summary>
    /// Api endpoint for Paying Invoice via Post Request, by sending nut19 payment payload.
    /// </summary>
    /// <param name="paymentPayload"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    [EnableCors(CorsPolicies.All)]
    [AllowAnonymous]
    [HttpPost("~/cashu/pay-invoice-pr")]
    public async Task<ActionResult> PayByPaymentRequest([FromBody] JObject payload)
    {
        try
        {
            if (payload["mint"] == null || payload["id"] == null || payload["unit"] == null || payload["proofs"] == null)
            {
                throw new ArgumentException("Required fields are missing in the payload.");
            }

            //todo idk why i didn't do it that way, but well i've fucked up
            //it's a workaround - it should be deserialized by JSONSerializer 
            var parsedPayload = new PaymentRequestPayload
            {
                Mint = payload["mint"].Value<string>(),
                PaymentId = payload["id"].Value<string>(),
                Memo = payload["memo"]?.Value<string>(),
                Unit = payload["unit"].Value<string>(),
                Proofs = payload["proofs"].Value<JArray>().Select(p => new Proof
                {
                    Amount = p["amount"]!.Value<ulong>(),
                    Id = new KeysetId(p["id"]!.Value<string>()),
                    Secret = new StringSecret(p["secret"]!.Value<string>()), 
                    C = new PubKey(p["C"]!.Value<string>()), 
                }).ToArray()
            };
            // var parsedPayload = JsonSerializer.Deserialize<PaymentRequestPayload>(paymentPayload);
            //   "id": str <optional>, will correspond to invoiceId
            //   "memo": str <optional>, idc about this
            //   "mint": str, //if trusted mint - save to db, if not - melt 🔥
            //   "unit": <str_enum>, should always be in sat, since there aren't any standardisation for unit denomination
            //   "proofs": Array<Proof>  yeah proofs

            var token = new CashuToken
            {
                Tokens =
                [
                    new CashuToken.Token
                    {
                        Mint = parsedPayload.Mint,
                        Proofs = parsedPayload.Proofs.ToList()
                    }
                ],
                Memo = parsedPayload.Memo,
                Unit = parsedPayload.Unit
            };

            await _cashuPaymentService.ProcessPaymentAsync(token, parsedPayload.PaymentId);
            return Ok("Payment sent!");
        }
        catch (CashuPaymentException cex)
        {
            return BadRequest($"Payment Error: {cex.Message}");
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // todo this method is useless,
    // check for `GetInvoicePaymentMethods` in `GreenfieldInvoiceController`
    // if you look `ToPaymentMethodModels`, you can see that you can add fields in the `AdditionalData` field
    // it may already be exposed if you already implemented `ParsePaymentPromptDetails`
    // `api/v1/stores/{storeId}/invoices/{invoiceId}/payment-methods`
    // API to fetch payment request for invoice
    [HttpGet("~/cashu/get-payment-request")]
    public Task<IActionResult> GetPaymentRequest()
    {
        throw new NotImplementedException();
    }
}
//todo make sure to reject stuff without active node lol 


  



