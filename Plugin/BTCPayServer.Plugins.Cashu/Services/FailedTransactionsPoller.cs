using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.enums;
using BTCPayServer.Plugins.Cashu.Data.Models;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Cashu.Services;

public class FailedTransactionsPoller(
    CashuDbContextFactory dbContextFactory,
    CashuPaymentService cashuPaymentService,
    CashuPaymentRegistrar cashuPaymentRegistrar,
    StoreRepository storeRepository,
    ILogger<FailedTransactionsPoller> logger,
    CashuMeltHandler meltHandler,
    CashuSwapHandler swapHandler
) : IHostedService, IDisposable
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Max transactions to poll in a single cycle.
    /// </summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>
    /// Max concurrent polls per mint to avoid overwhelming a single mint.
    /// </summary>
    public int MaxConcurrencyPerMint { get; init; } = 3;

    /// <summary>
    /// Max retries before giving up on a failed transaction.
    /// </summary>
    public int MaxRetries { get; init; } = 20;

    private static readonly TimeSpan MaxBackoffDelay = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _mintSemaphores = new();

    // Per-ftx lock to serialize concurrent polls (auto-poller + manual retry from UI/API).
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _ftxLocks = new();

    private readonly SemaphoreSlim _pollGuard = new(1, 1);
    private CancellationTokenSource? _cts;
    private Timer? _timer;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _timer = new Timer(
            _ => _ = PollAllAsync(_cts.Token),
            null,
            TimeSpan.FromSeconds(30), // initial delay
            PollInterval
        );
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Polls a single failed transaction and handles the result.
    /// Can be called from the controller for manual polling.
    /// Returns the poll result so callers can act on it (e.g. show UI feedback).
    /// </summary>
    public async Task<PollResult> PollTransaction(
        FailedTransaction ftx,
        CancellationToken ct = default)
    {
        if (ftx.Resolved)
            return new PollResult { State = CashuPaymentState.Success };

        var lockSem = _ftxLocks.GetOrAdd(ftx.Id, _ => new SemaphoreSlim(1, 1));
        await lockSem.WaitAsync(ct);
        try
        {
            // Re-read inside the lock — another thread may have resolved this ftx, or bumped
            // RetryCount/LastRetried since the caller loaded their snapshot. Using the caller's
            // stale counter would let us overwrite a concurrent increment and misjudge MaxRetries.
            await using (var checkDb = dbContextFactory.CreateContext())
            {
                var current = await checkDb.FailedTransactions
                    .AsNoTracking()
                    .SingleOrDefaultAsync(t => t.Id == ftx.Id, ct);

                if (current is null)
                    return new PollResult
                    {
                        State = CashuPaymentState.Failed,
                        Error = new InvalidOperationException($"Failed transaction {ftx.Id} no longer exists")
                    };

                if (current.Resolved)
                {
                    ftx.Resolved = true;
                    ftx.Details = current.Details;
                    ftx.RetryCount = current.RetryCount;
                    ftx.LastRetried = current.LastRetried;
                    return new PollResult { State = CashuPaymentState.Success };
                }

                ftx.RetryCount = current.RetryCount;
                ftx.LastRetried = current.LastRetried;
                ftx.Details = current.Details;
            }

            var storeData = await storeRepository.FindStore(ftx.StoreId);
            if (storeData == null)
                return new PollResult
                {
                    State = CashuPaymentState.Failed,
                    Error = new InvalidOperationException($"Store {ftx.StoreId} not found")
                };

            var result = ftx.OperationType == OperationType.Melt
                ? await meltHandler.PollFailed(ftx, storeData, ct)
                : await swapHandler.PollFailed(ftx, ct);

            await using var db = dbContextFactory.CreateContext();
            db.FailedTransactions.Attach(ftx);

            ftx.RetryCount++;
            ftx.LastRetried = DateTimeOffset.UtcNow;

            switch (result.State)
            {
                case CashuPaymentState.Success:
                    if (result.ResultProofs != null)
                    {
                        await cashuPaymentRegistrar.AddProofsToDb(
                            result.ResultProofs,
                            ftx.StoreId,
                            ftx.MintUrl,
                            ProofState.Available
                        );
                    }

                    // Register payment inside the same lock window. If this throws on a transient
                    // error (mint HTTP, DB), ftx stays unresolved — proofs are already safe in DB
                    // (idempotent insert) and the next retry replays cleanly.
                    var registration = await cashuPaymentRegistrar.RegisterPaymentForFailedTx(ftx, ct);
                    switch (registration)
                    {
                        case CashuPaymentRegistrar.FtxPaymentRegistrationResult.Registered:
                            ftx.Resolved = true;
                            ftx.Details = "Resolved by poller";
                            logger.LogInformation(
                                "(Cashu) Resolved failed tx {Id} (Invoice: {InvoiceId})",
                                ftx.Id, ftx.InvoiceId);
                            break;
                        case CashuPaymentRegistrar.FtxPaymentRegistrationResult.InvoiceMissing:
                            ftx.Resolved = true;
                            ftx.Details = "Invoice no longer exists; proofs retained, no payment to register";
                            logger.LogWarning(
                                "(Cashu) ftx {Id} resolved but invoice {InvoiceId} is gone — proofs retained",
                                ftx.Id, ftx.InvoiceId);
                            break;
                        case CashuPaymentRegistrar.FtxPaymentRegistrationResult.UnresolvableAmount:
                            ftx.Resolved = true;
                            ftx.Details = "Cannot derive payment amount — manual registration required";
                            logger.LogError(
                                "(Cashu) ftx {Id} (Invoice: {InvoiceId}) needs manual payment registration — amount unresolvable",
                                ftx.Id, ftx.InvoiceId);
                            break;
                    }
                    break;

                case CashuPaymentState.Failed:
                    ftx.Resolved = true;
                    ftx.Details = result.Error?.Message ?? "Permanently failed";
                    logger.LogWarning(
                        "(Cashu) Failed tx {Id} is permanently failed: {Details}",
                        ftx.Id, ftx.Details);
                    break;

                case CashuPaymentState.Pending:
                    if (ftx.RetryCount >= MaxRetries)
                    {
                        ftx.Resolved = true;
                        ftx.Details = $"Gave up after {MaxRetries} retries";
                        logger.LogWarning(
                            "(Cashu) Giving up on failed tx {Id} (Invoice: {InvoiceId}) after {MaxRetries} retries",
                            ftx.Id, ftx.InvoiceId, MaxRetries);
                    }
                    else
                    {
                        ftx.Details = result.Error?.Message ?? "Still pending";
                    }
                    break;
            }

            await db.SaveChangesAsync(ct);
            return result;
        }
        finally
        {
            lockSem.Release();
        }
    }

    private async Task PollAllAsync(CancellationToken ct)
    {
        // Prevent overlapping cycles
        if (!_pollGuard.Wait(0))
            return;

        try
        {
            await using var db = dbContextFactory.CreateContext();
            var unresolvedTxs = await db.FailedTransactions
                .Where(ft => !ft.Resolved)
                .OrderBy(ft => ft.LastRetried)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (unresolvedTxs.Count == 0)
                return;

            var now = DateTimeOffset.UtcNow;
            var eligibleTxs = unresolvedTxs
                .Where(ft => now >= ft.LastRetried + GetBackoffDelay(ft.RetryCount))
                .ToList();

            if (eligibleTxs.Count == 0)
                return;

            logger.LogDebug("(Cashu) Polling {Count} unresolved failed transactions", eligibleTxs.Count);

            var byMint = eligibleTxs.GroupBy(ft => ft.MintUrl).ToList();
            var mintTasks = byMint.Select(group => PollMintGroupAsync(group.ToList(), ct));
            await Task.WhenAll(mintTasks);

            foreach (var group in byMint)
            {
                if (_mintSemaphores.TryRemove(group.Key, out var sem))
                    sem.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "(Cashu) Error during failed transaction poll cycle");
        }
        finally
        {
            _pollGuard.Release();
        }
    }

    private async Task PollMintGroupAsync(List<FailedTransaction> txs, CancellationToken ct)
    {
        var semaphore = _mintSemaphores.GetOrAdd(txs[0].MintUrl, _ => new SemaphoreSlim(MaxConcurrencyPerMint));

        var tasks = txs.Select(async ftx =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await PollTransaction(ftx, ct);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "(Cashu) Error polling failed tx {Id}", ftx.Id);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private TimeSpan GetBackoffDelay(int retryCount)
    {
        var ticks = (long)(PollInterval.Ticks * Math.Pow(2, retryCount));
        return ticks > MaxBackoffDelay.Ticks ? MaxBackoffDelay : TimeSpan.FromTicks(ticks);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _cts?.Dispose();
        _pollGuard.Dispose();
        foreach (var sem in _mintSemaphores.Values)
            sem.Dispose();
        foreach (var sem in _ftxLocks.Values)
            sem.Dispose();
    }
}
