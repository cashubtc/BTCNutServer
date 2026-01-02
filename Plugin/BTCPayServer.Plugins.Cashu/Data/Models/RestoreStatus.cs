namespace BTCPayServer.Plugins.Cashu.Data.Models;

public enum RestoreState
{
    Queued,
    Processing,
    Completed,
    Cancelled,
    CompletedWithErrors,
    Failed
}