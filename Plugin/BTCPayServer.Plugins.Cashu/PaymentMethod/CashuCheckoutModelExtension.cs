using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;

namespace BTCPayServer.Plugins.Cashu.PaymentMethod;

public class CashuCheckoutModelExtension(DisplayFormatter displayFormatter)
    : ICheckoutModelExtension
{
    public const string CheckoutBodyComponentName = "CashuCheckout";

    public PaymentMethodId PaymentMethodId => CashuPlugin.CashuPmid;
    public string Image => "Resources/img/cashu.svg";
    public string Badge => "🥜";

    public void ModifyCheckoutModel(CheckoutModelContext context)
    {
        if (context is not { Handler: CashuPaymentMethodHandler handler })
            return;
        context.Model.CheckoutBodyComponentName = CheckoutBodyComponentName;
        //Cashu melt can take quite a long time - sometimes mint needs a long time to pay invoice.
        context.Model.ExpirationSeconds = int.MaxValue;
        context.Model.Activated = true;
        context.Model.InvoiceBitcoinUrl = context.Prompt.Destination;
        context.Model.InvoiceBitcoinUrlQR = context.Prompt.Destination;
        //Since cashu shouldn't be used with large amounts, let's stick to sats
        //For now there are no cashu wallets which would support this. Hopefully it can change in future
        context.Model.ShowPayInWalletButton = false;

        BitcoinCheckoutModelExtension.PreparePaymentModelForAmountInSats(
            context.Model,
            context.Prompt.Rate,
            displayFormatter
        );
    }
}
