using BTCPayServer.Plugins.Cashu.Data.enums;

namespace BTCPayServer.Plugins.Cashu.ViewModels;

public class CashuStoreViewModel
{
    public bool Enabled { get; set; }

    public CashuPaymentModel PaymentAcceptanceModel { get; set; }

    public string TrustedMintsUrls { get; set; } // newline or space separated URLs, with trailing slash removed

    public bool HasLightningNodeConnected { get; set; }
}
