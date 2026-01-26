using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using DotNut;

namespace BTCPayServer.Plugins.Cashu.Data.Models;

public class FailedTransaction
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }
    public required string InvoiceId { get; set; }
    public required string StoreId { get; set; }
    public required string MintUrl { get; set; }
    public required string Unit { get; set; }

    // JSON serialized input proofs (customer's proofs that were sent to mint)
    public string? InputProofsJson { get; set; }
    public ulong InputAmount { get; set; }

    [NotMapped]
    public Proof[] InputProofs
    {
        get => string.IsNullOrEmpty(InputProofsJson)
            ? []
            : JsonSerializer.Deserialize<Proof[]>(InputProofsJson) ?? [];
        set
        {
            InputProofsJson = JsonSerializer.Serialize(value);
            InputAmount = value.Aggregate(0UL, (sum, p) => sum + p.Amount);
        }
    }

    public required OperationType OperationType { get; set; }
    // For melt operation these will be for fee return. For swap these will contain outputs sent to mint.
    public required CashuUtils.OutputData OutputData { get; set; }
    public MeltDetails? MeltDetails { get; set; }
    public required int RetryCount { get; set; }
    public required DateTimeOffset LastRetried { get; set; }
    public string? Details { get; set; }
    public bool Resolved { get; set; }
}

public class MeltDetails
{
    public required string MeltQuoteId { get; set; }
    public required DateTimeOffset Expiry { get; set; }
    public required string LightningInvoiceId { get; set; }
    public required string Status { get; set; }

}


public enum OperationType
{
    Swap,
    Melt
}