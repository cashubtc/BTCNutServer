using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.Controllers.Greenfield.DTOs;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.enums;
using BTCPayServer.Plugins.Cashu.PaymentHandlers;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Cashu.Controllers.Greenfield;

[ApiController]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
[EnableCors(CorsPolicies.All)]
public class GreenfieldCashuConfigController(
    StoreRepository storeRepository,
    PaymentMethodHandlerDictionary handlers,
    CashuStatusProvider cashuStatusProvider,
    CashuDbContextFactory cashuDbContextFactory
) : ControllerBase
{
    private StoreData StoreData => HttpContext.GetStoreDataOrNull();

    [HttpGet("~/api/v1/stores/{storeId}/cashu")]
    [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> GetConfig(string storeId)
    {
        var config = StoreData.GetPaymentMethodConfig<CashuPaymentMethodConfig>(
            CashuPlugin.CashuPmid,
            handlers
        );

        var enabled = await cashuStatusProvider.CashuEnabled(storeId);

        return Ok(new CashuConfigResponseDto(
            Enabled: enabled,
            PaymentModel: config?.PaymentModel ?? CashuPaymentModel.TrustedMintsOnly,
            TrustedMintsUrls: config?.TrustedMintsUrls ?? [],
            FeeConfig: config?.FeeConfing is { } fee
                ? new CashuFeeConfigDto(fee.MaxKeysetFee, fee.MaxLightningFee, fee.CustomerFeeAdvance)
                : null
        ));
    }

    [HttpPut("~/api/v1/stores/{storeId}/cashu")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> UpdateConfig(string storeId, [FromBody] UpdateCashuConfigRequestDto request)
    {
        var store = StoreData;
        var blob = store.GetStoreBlob();
        var lightningEnabled = store.IsLightningEnabled("BTC");

        var current = store.GetPaymentMethodConfig<CashuPaymentMethodConfig>(
            CashuPlugin.CashuPmid,
            handlers
        );

        var parsedMints = (request.TrustedMintsUrls ?? current?.TrustedMintsUrls ?? [])
            .Select(u => u.Trim())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(MintManager.NormalizeMintUrl)
            .ToList();

        var paymentModel = request.PaymentModel ?? current?.PaymentModel ?? CashuPaymentModel.TrustedMintsOnly;

        if (!lightningEnabled && paymentModel is CashuPaymentModel.HoldWhenTrusted or CashuPaymentModel.AutoConvert)
        {
            paymentModel = CashuPaymentModel.TrustedMintsOnly;
        }
        var feeConfig = new CashuFeeConfig
        {
            MaxKeysetFee = request.FeeConfig?.MaxKeysetFee ?? current?.FeeConfing?.MaxKeysetFee ?? 3,
            MaxLightningFee = request.FeeConfig?.MaxLightningFee ?? current?.FeeConfing?.MaxLightningFee ?? 3,
            CustomerFeeAdvance = request.FeeConfig?.CustomerFeeAdvance ?? current?.FeeConfing?.CustomerFeeAdvance ?? 3,
        };

        var newConfig = new CashuPaymentMethodConfig
        {
            PaymentModel = paymentModel,
            TrustedMintsUrls = parsedMints,
            FeeConfing = feeConfig,
        };

        if (request.Enabled is true)
        {
            await using var db = cashuDbContextFactory.CreateContext();
            var hasWallet = await db.CashuWalletConfig
                .AnyAsync(c => c.StoreId == storeId && c.WalletMnemonic != null);

            if (!hasWallet)
            {
                return this.CreateAPIError(404, "paymentmethod-not-configured",
                    "The Cashu payment method is not configured. Set up a wallet first via POST /api/v1/stores/{storeId}/cashu/wallet.");
            }
        }

        if (request.Enabled.HasValue)
        {
            blob.SetExcluded(CashuPlugin.CashuPmid, !request.Enabled.Value);
        }

        store.SetPaymentMethodConfig(handlers[CashuPlugin.CashuPmid], newConfig);
        store.SetStoreBlob(blob);
        await storeRepository.UpdateStore(store);

        return await GetConfig(storeId);
    }
}
