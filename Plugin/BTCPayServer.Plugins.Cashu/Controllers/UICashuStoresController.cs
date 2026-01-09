#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.enums;
using BTCPayServer.Plugins.Cashu.PaymentHandlers;
using BTCPayServer.Plugins.Cashu.ViewModels;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BTCPayServer.Services.Invoices;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Cashu.Controllers;

[Route("stores")]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UICashuStoresController : Controller
{
    public UICashuStoresController(
        StoreRepository storeRepository,
        CashuDbContextFactory cashuDbContextFactory,
        PaymentMethodHandlerDictionary handlers,
        CashuStatusProvider cashuStatusProvider)
    {
        _storeRepository = storeRepository;
        _cashuDbContextFactory = cashuDbContextFactory;
        _handlers = handlers;
        _cashuStatusProvider = cashuStatusProvider;
    }

    private StoreData StoreData => HttpContext.GetStoreData();

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
        var cashuPaymentMethodConfig =
            StoreData.GetPaymentMethodConfig<CashuPaymentMethodConfig>(CashuPlugin.CashuPmid, _handlers);
        {
            await using var db = _cashuDbContextFactory.CreateContext();
            var config = db.CashuWalletConfig.FirstOrDefault(cwc => cwc.StoreId == StoreData.Id);
            if (config == null)
            {
                return RedirectToAction("GettingStarted", "UICashuOnboarding", new { storeId = StoreData.Id });
            }

            if (!config.Verified)
            {
                return RedirectToAction("ConfirmMnemonic", "UICashuOnboarding", new { storeId = StoreData.Id });
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

        return View("Views/Cashu/StoreConfig.cshtml", model);
    }

    /// <summary>
    /// Api route for setting plugin configuration for this store
    /// </summary>
    [HttpPost("{storeId}/cashu")]
    public async Task<IActionResult> StoreConfig(string storeId, CashuStoreViewModel viewModel)
    {
        var store = StoreData;
        var blob = store.GetStoreBlob();
        viewModel.TrustedMintsUrls ??= "";

        //trimming trailing slash
        var parsedTrustedMintsUrls = viewModel.TrustedMintsUrls
            .Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimEnd('/'))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        var lightningEnabled = StoreData.IsLightningEnabled("BTC");

        //todo make sure to enable new models
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

        return RedirectToAction("StoreConfig", new { storeId = store.Id });
    }

    [HttpGet("{storeId}/cashu/settings")]
    public async Task<IActionResult> Settings(string storeId)
    {
        var storeConfig = StoreData.GetPaymentMethodConfig<CashuPaymentMethodConfig>(CashuPlugin.CashuPmid, _handlers);
        if (storeConfig == null)
        {
            return RedirectToAction("StoreConfig", new { storeId = StoreData.Id });
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

        return View("Views/Cashu/Settings/FeeSettings.cshtml", model);
    }
    
    [HttpPost("{storeId}/cashu/settings")]
    public async Task<IActionResult> Settings(string storeId, CashuSettingsViewModel viewModel)
    {
        var config = StoreData.GetPaymentMethodConfig<CashuPaymentMethodConfig>(CashuPlugin.CashuPmid, _handlers);
        if (config == null)
        {
            return RedirectToAction("GettingStarted", "UICashuOnboarding", new { storeId = StoreData.Id });
        }
        config.FeeConfing.CustomerFeeAdvance = viewModel.CustomerFeeAdvance;
        config.FeeConfing.MaxLightningFee = viewModel.MaxLightningFee;
        config.FeeConfing.MaxKeysetFee = viewModel.MaxKeysetFee;

        StoreData.SetPaymentMethodConfig(_handlers[CashuPlugin.CashuPmid], config);
        TempData[WellKnownTempData.SuccessMessage] = "Settings saved successfully";
        return View("Views/Cashu/Settings/FeeSettings.cshtml", viewModel);
    }

    [HttpGet("{storeId}/cashu/remove-wallet")]
    public async Task<IActionResult> GetRemoveWallet(string storeId)
    {
        return View("Views/Cashu/Settings/RemoveWallet.cshtml");
    }

    // todo make sure that user know what he's doing, he can lose his money here
    [HttpDelete("{storeId}/cashu/remove-wallet")]
    public async Task<IActionResult> RemoveWallet(string storeId)
    {
        if (StoreData?.Id == null)
        {
            return NotFound();
        }
        // remove wallet config
        await using var db = _cashuDbContextFactory.CreateContext();
        var currentConfig = db.CashuWalletConfig.Where(cwc => cwc.StoreId == StoreData.Id);
        await currentConfig.ExecuteDeleteAsync();

        // remove config and turn off cashu payment method
        var blob = StoreData.GetStoreBlob();
        blob.SetExcluded(CashuPlugin.CashuPmid, true);
        StoreData.SetStoreBlob(blob);

        await _storeRepository.UpdateStore(StoreData);
        TempData[WellKnownTempData.SuccessMessage] = "Wallet removed successfully";
        return Ok();
    }
}
