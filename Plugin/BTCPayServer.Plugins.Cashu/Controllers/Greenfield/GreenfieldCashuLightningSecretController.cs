using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Cashu.Controllers.Greenfield;

[ApiController]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
[EnableCors(CorsPolicies.All)]
public class GreenfieldCashuLightningSecretController(
    CashuDbContextFactory cashuDbContextFactory
) : ControllerBase
{
    [HttpPost("~/api/v1/stores/{storeId}/cashu/lightning-client-secret")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> GenerateLightningClientSecret(string storeId)
    {
        await using var db = cashuDbContextFactory.CreateContext();
        var walletConfig = await db.CashuWalletConfig.SingleOrDefaultAsync(c => c.StoreId == storeId);

        if (walletConfig is null)
        {
            return this.CreateAPIError(404, "wallet-not-found", 
                "No Cashu wallet configured for this store.");
        }
        if (walletConfig.LightningClientSecret is not null)
        {
            return this.CreateAPIError("secret-already-exists", 
                "A secret already exists. Use PUT to rotate it.");
        }
        walletConfig.LightningClientSecret = Guid.NewGuid();
        await db.SaveChangesAsync();

        return Ok(new LightningClientSecretResponseDto(Secret: walletConfig.LightningClientSecret.Value));
    }

    [HttpPut("~/api/v1/stores/{storeId}/cashu/lightning-client-secret")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> RotateLightningClientSecret(string storeId)
    {
        await using var db = cashuDbContextFactory.CreateContext();
        var walletConfig = await db.CashuWalletConfig.SingleOrDefaultAsync(c => c.StoreId == storeId);

        if (walletConfig is null)
        {
            return this.CreateAPIError(404, "wallet-not-found", 
                "No Cashu wallet configured for this store.");
        }
        walletConfig.LightningClientSecret = Guid.NewGuid();
        await db.SaveChangesAsync();

        return Ok(new LightningClientSecretResponseDto(Secret: walletConfig.LightningClientSecret.Value));
    }

    [HttpDelete("~/api/v1/stores/{storeId}/cashu/lightning-client-secret")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> RevokeLightningClientSecret(string storeId)
    {
        await using var db = cashuDbContextFactory.CreateContext();
        var walletConfig = await db.CashuWalletConfig.SingleOrDefaultAsync(c => c.StoreId == storeId);

        if (walletConfig is null)
        {
            return this.CreateAPIError(404, "wallet-not-found", 
                "No Cashu wallet configured for this store.");
        }
        walletConfig.LightningClientSecret = null;
        await db.SaveChangesAsync();

        return Ok();
    }
}
