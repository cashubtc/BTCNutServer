using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.enums;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Cashu.PaymentHandlers;

public class CashuStatusProvider(
    StoreRepository storeRepository,
    PaymentMethodHandlerDictionary handlers,
    CashuDbContextFactory cashuDbContextFactory
)
{
    public async Task<bool> CashuEnabled(string storeId)
    {
        try
        {
            var storeData = await storeRepository.FindStore(storeId);

            var currentPaymentMethodConfig =
                storeData?.GetPaymentMethodConfig<CashuPaymentMethodConfig>(
                    CashuPlugin.CashuPmid,
                    handlers
                );
            if (currentPaymentMethodConfig == null)
            {
                return false;
            }

            if (
                currentPaymentMethodConfig.PaymentModel
                is CashuPaymentModel.HoldWhenTrusted
                    or CashuPaymentModel.AutoConvert
            )
            {
                if (!storeData.IsLightningEnabled("BTC"))
                {
                    return false;
                }
            }

            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();
            if (excludeFilters.Match(CashuPlugin.CashuPmid))
            {
                return false;
            }

            await using var db = cashuDbContextFactory.CreateContext();
            var hasWallet = await db.CashuWalletConfig
                .AnyAsync(c => c.StoreId == storeId && c.WalletMnemonic != null);

            return hasWallet;
        }
        catch
        {
            return false;
        }
    }
}
