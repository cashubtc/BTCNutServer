namespace BTCPayServer.Plugins.Cashu.Data.enums;

public enum CashuPaymentModel
{
    // trusted mints only
    TrustedMintsOnly,
    // Hold When Trusted - name is already in payment method config. not sure if changing this won't break current implementations
    HoldWhenTrusted,
    AutoConvert
}