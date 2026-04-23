using System.Collections.Generic;

namespace BTCPayServer.Plugins.Cashu.Data;

public static class FailedTransactionReasons
{
    public const string SwapMintConnectionBroken = "swap_mint_connection_broken";
    public const string SwapMintReturnedLessSignatures = "swap_mint_returned_less_signatures";
    public const string MeltPendingAfterRetry = "melt_pending_after_retry";
    public const string MeltRetryMintUnreachable = "melt_retry_mint_unreachable";
    public const string MeltSaveProofsFailed = "melt_save_proofs_failed";
    public const string ResolvedByPoller = "resolved_by_poller";
    public const string InvoiceMissing = "invoice_missing";
    public const string UnresolvableAmount = "unresolvable_amount";
    public const string PermanentlyFailed = "permanently_failed";
    public const string StillPending = "still_pending";
    public const string MaxRetriesExceeded = "max_retries_exceeded";
    public const string DismissedByUser = "dismissed_by_user";
    public const string MissingMeltDetails = "missing_melt_details";
    public const string LegacyUnknown = "legacy_unknown";
    public const string LegacyPending = "legacy_pending";

    public static readonly IReadOnlyDictionary<string, string> Descriptions =
        new Dictionary<string, string>
        {
            [SwapMintConnectionBroken] = "Connection with mint broken while swap.",
            [SwapMintReturnedLessSignatures] = "Mint returned fewer signatures than requested.",
            [MeltPendingAfterRetry] = "Melt is still pending after retry.",
            [MeltRetryMintUnreachable] = "Mint was unreachable during melt retry.",
            [MeltSaveProofsFailed] = "Melt succeeded at the mint but local proof persistence failed.",
            [ResolvedByPoller] = "Resolved by poller.",
            [InvoiceMissing] = "Invoice no longer exists; proofs retained, no payment to register.",
            [UnresolvableAmount] = "Cannot derive payment amount; manual registration required.",
            [PermanentlyFailed] = "Transaction failed permanently.",
            [StillPending] = "Transaction is still pending.",
            [MaxRetriesExceeded] = "Automatic retries were exhausted.",
            [DismissedByUser] = "Dismissed by merchant.",
            [MissingMeltDetails] = "Legacy or invalid melt transaction is missing melt details.",
            [LegacyUnknown] = "Legacy failed transaction imported before structured status tracking.",
            [LegacyPending] = "Legacy pending transaction imported before structured status tracking.",
        };

    public static string Describe(string reason) =>
        Descriptions.TryGetValue(reason, out var description)
            ? description
            : reason;
}
