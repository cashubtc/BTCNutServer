using DotNut.ApiModels;

namespace BTCPayServer.Plugins.Cashu.Data.Models;

public class LightningClientQuote
{
    public PostMeltQuoteBolt11Response quote { get; set; }
}
