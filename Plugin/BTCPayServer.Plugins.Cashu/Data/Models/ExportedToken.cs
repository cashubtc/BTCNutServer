using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.Plugins.Cashu.Data.Models;

public record ExportedToken
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; } = new();
    public string SerializedToken { get; set; }
    public ulong Amount { get; set; }
    public string Unit { get; set; }
    public string Mint { get; set; }

    public List<StoredProof> Proofs { get; set; } = [];

    public bool IsUsed { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTime.UtcNow;
    public string StoreId { get; set; }
};
