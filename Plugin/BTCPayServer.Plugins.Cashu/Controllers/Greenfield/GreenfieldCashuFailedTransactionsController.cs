using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Plugins.Cashu.Controllers.Greenfield.DTOs;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.Models;
using BTCPayServer.Plugins.Cashu.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Cashu.Controllers.Greenfield;

[ApiController]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
[EnableCors(CorsPolicies.All)]
public class GreenfieldCashuFailedTransactionsController(
    CashuDbContextFactory cashuDbContextFactory,
    InvoiceRepository invoiceRepository,
    FailedTransactionsPoller failedTransactionsPoller
) : ControllerBase
{
    [HttpGet("~/api/v1/stores/{storeId}/cashu/failed-transactions")]
    [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> GetFailedTransactions(string storeId)
    {
        await using var db = cashuDbContextFactory.CreateContext();

        var transactions = await db.FailedTransactions
            .Where(ft => ft.StoreId == storeId)
            .Where(ft => ft.Status != FailedTransactionStatus.Dismissed)
            .OrderByDescending(ft => ft.CreatedAt)
            .ToListAsync();

        return Ok(transactions.Select(ToResponse));
    }

    [HttpPost("~/api/v1/stores/{storeId}/cashu/failed-transactions/{failedTransactionId}/retry")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> RetryFailedTransaction(string storeId, Guid failedTransactionId)
    {
        await using var db = cashuDbContextFactory.CreateContext();

        var failedTransaction = await db.FailedTransactions
            .SingleOrDefaultAsync(t => t.Id == failedTransactionId && t.StoreId == storeId);

        if (failedTransaction is null)
        {
            return this.CreateAPIError(404, "failed-transaction-not-found", "The failed transaction was not found.");
        }

        if (failedTransaction.Status == FailedTransactionStatus.Recovered)
        {
            return Ok(ToResponse(failedTransaction));
        }
        if (failedTransaction.Status == FailedTransactionStatus.Dismissed)
        {
            return this.CreateAPIError(409, "failed-transaction-dismissed", "The failed transaction is dismissed.");
        }

        var invoice = await invoiceRepository.GetInvoice(failedTransaction.InvoiceId);
        if (invoice is null)
        {
            return this.CreateAPIError(404, "invoice-not-found",
                "Invoice associated with this transaction was not found.");
        }
        PollResult pollResult;
        try
        {
            pollResult = await failedTransactionsPoller.PollTransaction(failedTransaction);
        }
        catch (Exception ex)
        {
            return this.CreateAPIError("poll-failed", $"Could not poll transaction: {ex.Message}");
        }

        if (!pollResult.Success)
        {
            var errorMsg = pollResult.Error?.Message;
            return this.CreateAPIError(
                "transaction-not-resolved",
                $"Transaction state: {pollResult.State}.{(errorMsg is not null ? " " + errorMsg : "")}"
            );
        }

        return Ok(ToResponse(failedTransaction));
    }

    [HttpDelete("~/api/v1/stores/{storeId}/cashu/failed-transactions/{failedTransactionId}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> DismissFailedTransaction(string storeId, Guid failedTransactionId)
    {
        await using var db = cashuDbContextFactory.CreateContext();

        var failedTransaction = await db.FailedTransactions
            .SingleOrDefaultAsync(t => t.Id == failedTransactionId && t.StoreId == storeId);

        if (failedTransaction is null)
        {
            return this.CreateAPIError(404, "failed-transaction-not-found", "The failed transaction was not found.");
        }

        failedTransaction.Status = FailedTransactionStatus.Dismissed;
        failedTransaction.ReasonCode = FailedTransactionReasons.DismissedByUser;
        failedTransaction.Details = FailedTransactionReasons.Describe(FailedTransactionReasons.DismissedByUser);
        failedTransaction.DismissedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return Ok(ToResponse(failedTransaction));
    }

    private static FailedTransactionResponseDto ToResponse(FailedTransaction t) => new(
        Id: t.Id,
        InvoiceId: t.InvoiceId,
        MintUrl: t.MintUrl,
        Unit: t.Unit,
        InputAmount: t.InputAmount,
        OperationType: t.OperationType.ToString(),
        CreatedAt: t.CreatedAt,
        RetryCount: t.RetryCount,
        LastRetried: t.LastRetried,
        Status: t.Status.ToString(),
        CanRetry: t.CanRetryManually,
        CanDismiss: t.CanDismiss,
        ReasonCode: t.ReasonCode,
        Details: t.Details
    );
}
