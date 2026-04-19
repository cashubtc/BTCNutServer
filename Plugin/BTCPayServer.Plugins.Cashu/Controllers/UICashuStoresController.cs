using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.enums;
using BTCPayServer.Plugins.Cashu.PaymentHandlers;
using BTCPayServer.Plugins.Cashu.ViewModels;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Cashu.Controllers;

[Route("stores")]
[Authorize(
    Policy = Policies.CanModifyStoreSettings,
    AuthenticationSchemes = AuthenticationSchemes.Cookie
)]
public class UICashuStoresController : Controller
{
    public UICashuStoresController(
        StoreRepository storeRepository,
        CashuDbContextFactory cashuDbContextFactory,
        PaymentMethodHandlerDictionary handlers,
        CashuStatusProvider cashuStatusProvider
    )
    {
        _storeRepository = storeRepository;
        _cashuDbContextFactory = cashuDbContextFactory;
        _handlers = handlers;
        _cashuStatusProvider = cashuStatusProvider;
    }

    private StoreData? StoreData => HttpContext.GetStoreDataOrNull();

    private readonly StoreRepository _storeRepository;
    private readonly CashuDbContextFactory _cashuDbContextFactory;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly CashuStatusProvider _cashuStatusProvider;

    /// <summary>
    /// Api route for fetching current plugin configuration for this store
    /// </summary>
    [HttpGet("{storeId}/cashu")]
    public async Task<IActionResult> StoreConfig(string storeId)
    {
        if (StoreData is not { } store) return NotFound();
        var cashuPaymentMethodConfig = store.GetPaymentMethodConfig<CashuPaymentMethodConfig>(
            CashuPlugin.CashuPmid,
            _handlers
        );
        {
            await using var db = _cashuDbContextFactory.CreateContext();
            var config = await db.CashuWalletConfig.FirstOrDefaultAsync(cwc => cwc.StoreId == store.Id);
            if (config == null)
            {
                return RedirectToAction(
                    "GettingStarted",
                    "UICashuOnboarding",
                    new { storeId = store.Id }
                );
            }

            if (!config.Verified)
            {
                return RedirectToAction(
                    "ConfirmMnemonic",
                    "UICashuOnboarding",
                    new { storeId = store.Id }
                );
            }
        }

        CashuStoreViewModel model = new CashuStoreViewModel();
        model.HasLightningNodeConnected = store.IsLightningEnabled("BTC");
        if (cashuPaymentMethodConfig == null)
        {
            model.Enabled = await _cashuStatusProvider.CashuEnabled(store.Id);
            model.PaymentAcceptanceModel = CashuPaymentModel.TrustedMintsOnly;
            model.TrustedMintsUrls = "";
        }
        else
        {
            model.Enabled = await _cashuStatusProvider.CashuEnabled(store.Id);
            model.PaymentAcceptanceModel = cashuPaymentMethodConfig.PaymentModel;
            model.TrustedMintsUrls = String.Join(
                ";",
                cashuPaymentMethodConfig.TrustedMintsUrls
            );
        }

        return View("Views/Cashu/StoreConfig.cshtml", model);
    }

    /// <summary>
    /// Api route for setting plugin configuration for this store
    /// </summary>
    [HttpPost("{storeId}/cashu")]
    public async Task<IActionResult> StoreConfig(string storeId, CashuStoreViewModel viewModel)
    {
        if (StoreData is not { } store) return NotFound();
        var blob = store.GetStoreBlob();
        viewModel.TrustedMintsUrls ??= "";

        var parsedTrustedMintsUrls = viewModel
            .TrustedMintsUrls.Split(["\r\n", "\r", "\n", ";"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(MintManager.NormalizeMintUrl)
            .ToList();

        var lightningEnabled = store.IsLightningEnabled("BTC");
        var currentSettings = store.GetPaymentMethodConfig<CashuPaymentMethodConfig>(
            CashuPlugin.CashuPmid,
            _handlers
        );

        //If lighting isn't configured - don't allow user to set meltImmediately.
        var paymentMethodConfig = new CashuPaymentMethodConfig
        {
            PaymentModel = lightningEnabled
                ? viewModel.PaymentAcceptanceModel
                : CashuPaymentModel.TrustedMintsOnly,
            TrustedMintsUrls = parsedTrustedMintsUrls,
            // 5, 5, and 5 sound like reasonable defaults
            FeeConfing =
                currentSettings?.FeeConfing
                ?? new CashuFeeConfig
                {
                    CustomerFeeAdvance = 3,
                    MaxLightningFee = 3,
                    MaxKeysetFee = 3,
                },
        };

        blob.SetExcluded(CashuPlugin.CashuPmid, !viewModel.Enabled);

        store.SetPaymentMethodConfig(_handlers[CashuPlugin.CashuPmid], paymentMethodConfig);
        store.SetStoreBlob(blob);
        await _storeRepository.UpdateStore(store);
        if (
            viewModel.PaymentAcceptanceModel == CashuPaymentModel.HoldWhenTrusted
            && !lightningEnabled
        )
        {
            TempData[WellKnownTempData.ErrorMessage] =
                "Can't use this payment model. Lightning wallet is disabled.";
        }
        else
        {
            TempData[WellKnownTempData.SuccessMessage] = "Config Saved Successfully";
        }

        return RedirectToAction("StoreConfig", new { storeId = store.Id });
    }

    [HttpGet("{storeId}/cashu/settings")]
    public async Task<IActionResult> Settings(string storeId)
    {
        if (StoreData is not { } store) return NotFound();
        var storeConfig = store.GetPaymentMethodConfig<CashuPaymentMethodConfig>(
            CashuPlugin.CashuPmid,
            _handlers
        );
        if (storeConfig == null)
        {
            return RedirectToAction("StoreConfig", new { storeId = store.Id });
        }
        var feeConfig =
            storeConfig.FeeConfing
            ?? new CashuFeeConfig
            {
                CustomerFeeAdvance = 0,
                MaxLightningFee = 0,
                MaxKeysetFee = 0,
            };

        await using var db = _cashuDbContextFactory.CreateContext();
        var walletConfig = await db.CashuWalletConfig.SingleOrDefaultAsync(cwc => cwc.StoreId == storeId);

        var model = new CashuSettingsViewModel
        {
            CustomerFeeAdvance = feeConfig.CustomerFeeAdvance,
            MaxLightningFee = feeConfig.MaxLightningFee,
            MaxKeysetFee = feeConfig.MaxKeysetFee,
            LightningClientSecret = walletConfig?.LightningClientSecret,
        };

        return View("Views/Cashu/CashuSettings.cshtml", model);
    }

    [HttpPost("{storeId}/cashu/settings")]
    public async Task<IActionResult> Settings(string storeId, CashuSettingsViewModel viewModel)
    {
        if (StoreData is not { } store) return NotFound();
        var config = store.GetPaymentMethodConfig<CashuPaymentMethodConfig>(
            CashuPlugin.CashuPmid,
            _handlers
        );
        if (config == null)
        {
            return RedirectToAction(
                "GettingStarted",
                "UICashuOnboarding",
                new { storeId = store.Id }
            );
        }

        config.FeeConfing ??= new CashuFeeConfig();
        config.FeeConfing.CustomerFeeAdvance = viewModel.CustomerFeeAdvance;
        config.FeeConfing.MaxLightningFee = viewModel.MaxLightningFee;
        config.FeeConfing.MaxKeysetFee = viewModel.MaxKeysetFee;

        store.SetPaymentMethodConfig(_handlers[CashuPlugin.CashuPmid], config);
        await _storeRepository.UpdateStore(store);
        TempData[WellKnownTempData.SuccessMessage] = "Settings saved successfully";
        return RedirectToAction(nameof(Settings), new { storeId });
    }

    [HttpPost("{storeId}/cashu/remove-wallet")]
    public async Task<IActionResult> RemoveWallet(string storeId)
    {
        if (StoreData is not { } store)
            return NotFound();
        var id = store.Id;
        // remove wallet config
        await using var db = _cashuDbContextFactory.CreateContext();
        var currentConfig = db.CashuWalletConfig.Where(cwc => cwc.StoreId == id);
        await currentConfig.ExecuteDeleteAsync();

        //remove proofs
        var proofsFromWallet = db.Proofs.Where(p => p.StoreId == id);
        await proofsFromWallet.ExecuteDeleteAsync();

        //remove exported tokens 
        var tokensFromWallet = db.ExportedTokens.Where(t => t.StoreId == id);
        await tokensFromWallet.ExecuteDeleteAsync();

        // remove config and turn off cashu payment method
        var blob = store.GetStoreBlob();
        blob.SetExcluded(CashuPlugin.CashuPmid, true);
        store.SetStoreBlob(blob);
        await _storeRepository.UpdateStore(store);

        // remove cashu lightning client payments and invoices
        var payments = db.LightningPayments.Where(p => p.StoreId == id);
        await payments.ExecuteDeleteAsync();

        var invoices = db.LightningInvoices.Where(p => p.StoreId == id);
        await invoices.ExecuteDeleteAsync();

        TempData[WellKnownTempData.SuccessMessage] = "Wallet removed successfully";
        return RedirectToAction("Dashboard", "UIStores", new { StoreId = store.Id });
    }

    [HttpPost("{storeId}/cashu/settings/lightning-client/generate")]
    public async Task<IActionResult> GenerateLightningClientSecret(string storeId)
    {
        await using var db = _cashuDbContextFactory.CreateContext();
        var walletConfig = await db.CashuWalletConfig.SingleOrDefaultAsync(cwc => cwc.StoreId == storeId);

        if (walletConfig == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "No Cashu wallet configured for this store.";
            return RedirectToAction(nameof(Settings), new { storeId });
        }

        if (walletConfig.LightningClientSecret is not null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "A secret already exists. Use rotate to replace it.";
            return RedirectToAction(nameof(Settings), new { storeId });
        }

        walletConfig.LightningClientSecret = Guid.NewGuid();
        await db.SaveChangesAsync();

        TempData[WellKnownTempData.SuccessMessage] = "Lightning client secret generated successfully.";
        return RedirectToAction(nameof(Settings), new { storeId });
    }

    [HttpPost("{storeId}/cashu/settings/lightning-client/rotate")]
    public async Task<IActionResult> RotateLightningClientSecret(string storeId)
    {
        await using var db = _cashuDbContextFactory.CreateContext();
        var walletConfig = await db.CashuWalletConfig.SingleOrDefaultAsync(cwc => cwc.StoreId == storeId);

        if (walletConfig == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "No Cashu wallet configured for this store.";
            return RedirectToAction(nameof(Settings), new { storeId });
        }

        walletConfig.LightningClientSecret = Guid.NewGuid();
        await db.SaveChangesAsync();

        TempData[WellKnownTempData.SuccessMessage] = "Lightning client secret rotated. Update any connections using the old secret.";
        return RedirectToAction(nameof(Settings), new { storeId });
    }
}
