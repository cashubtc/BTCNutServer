using System.Collections.Generic;

namespace BTCPayServer.Plugins.Cashu.ViewModels;

public class ConfirmMnemonicViewModel
{
    public string Mnemonic { get; set; }
    public List<string> Words { get; set; }
    public string ViewMnemonicUrl { get; set; }
    public string ReturnUrl { get; set; }
}