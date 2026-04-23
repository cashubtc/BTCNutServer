using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.PaymentMethod;
using BTCPayServer.Plugins.Cashu.Wallets;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Plugins.Cashu.Services;

public record CashuOperationContext(
    StatefulWallet Wallet,
    InvoiceEntity Invoice,
    StoreData Store,
    CashuUtils.SimplifiedCashuToken Token,
    CashuPaymentMethodConfig PaymentMethodConfig,
    LightMoney UnitValue,
    LightMoney Value);
