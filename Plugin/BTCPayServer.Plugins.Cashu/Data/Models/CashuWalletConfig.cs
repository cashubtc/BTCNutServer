using DotNut.NBitcoin.BIP39;

namespace BTCPayServer.Plugins.Cashu.Data.Models;

public class CashuWalletConfig
{
    public string StoreId { get; set; }
    public Mnemonic WalletMnemonic { get; set; }
}