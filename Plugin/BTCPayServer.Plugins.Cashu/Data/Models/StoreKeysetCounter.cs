using DotNut;

namespace BTCPayServer.Plugins.Cashu.Data.Models;

public class StoreKeysetCounter
{
    public string StoreId { get; set; }
    public KeysetId KeysetId { get; set; }
    public int Counter { get; set; }
}