using System;
using System.Collections.Generic;
using BTCPayServer.Plugins.Cashu.Data.enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Plugins.Cashu.Models;

public record CashuConfigResponseDto(
    bool Enabled,
    [property: JsonConverter(typeof(StringEnumConverter))] CashuPaymentModel PaymentModel,
    List<string> TrustedMintsUrls,
    CashuFeeConfigDto? FeeConfig
);

public record CashuFeeConfigDto(
    int MaxKeysetFee,
    int MaxLightningFee,
    int CustomerFeeAdvance
);

public record UpdateCashuConfigRequestDto(
    bool? Enabled,
    [property: JsonConverter(typeof(StringEnumConverter))] CashuPaymentModel? PaymentModel,
    List<string>? TrustedMintsUrls,
    CashuFeeConfigDto? FeeConfig
);

public record WalletCreatedResponseDto(string Mnemonic);

public record RestoreWalletRequestDto(string Mnemonic, List<string>? MintUrls);

public record RestoreStartedResponseDto(string? JobId);

public record RestoreStatusResponseDto(
    string JobId,
    string Status,
    int TotalMints,
    int ProcessedMints,
    List<string> UnreachableMints,
    List<string> Errors,
    DateTime QueuedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    List<RestoredMintResponseDto> RestoredMints
);

public record RestoredMintResponseDto(string MintUrl, Dictionary<string, ulong> Balances);

public record LightningClientSecretResponseDto(Guid Secret);

public record WalletBalanceItemDto(string Mint, string Unit, ulong Amount);

public record WalletResponseDto(
    List<WalletBalanceItemDto> Balances,
    List<string> UnavailableMints
);

public record CheckTokenStatesResponseDto(int MarkedSpent, List<string> FailedMints);

public record RemoveSpentProofsResponseDto(int Removed, List<string> FailedMints);

public record ExportBalanceRequestDto(string MintUrl, string Unit);

public record ExportedTokenResponseDto(
    Guid Id,
    ulong Amount,
    string Unit,
    string Mint,
    string Token,
    bool IsUsed,
    DateTimeOffset CreatedAt
);

public record FailedTransactionResponseDto(
    Guid Id,
    string InvoiceId,
    string MintUrl,
    string Unit,
    ulong InputAmount,
    string OperationType,
    int RetryCount,
    DateTimeOffset LastRetried,
    bool Resolved,
    string? Details
);
