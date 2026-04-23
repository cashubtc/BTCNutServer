using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.Cashu.ViewModels;

public class FailedTransactionsViewModel
{
    public required string StoreId { get; set; }
    public required List<FailedTransactionListItemViewModel> Transactions { get; set; }
}

public class FailedTransactionListItemViewModel
{
    public required Guid FailedTransactionId { get; set; }
    public required string InvoiceId { get; set; }
    public required string MintUrl { get; set; }
    public required string Unit { get; set; }
    public required ulong InputAmount { get; set; }
    public required string OperationLabel { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public required DateTimeOffset LastRetried { get; set; }
    public required int RetryCount { get; set; }
    public required string Status { get; set; }
    public required bool CanRetry { get; set; }
    public required bool CanDismiss { get; set; }
    public string? ReasonCode { get; set; }
    public string? ReasonLabel { get; set; }
    public string? Details { get; set; }
}
