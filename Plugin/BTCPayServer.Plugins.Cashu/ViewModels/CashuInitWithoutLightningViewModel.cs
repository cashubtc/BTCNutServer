using BTCPayServer.Plugins.Cashu.Data.enums;

namespace BTCPayServer.Plugins.Cashu.ViewModels;

public class CashuInitWithoutLightningViewModel
{
    public string TrustedMintsUrls { get; set; }
    public CashuPaymentModel PaymentAcceptanceModel { get; set; }
    public string ReturnUrl { get; set; }
}
