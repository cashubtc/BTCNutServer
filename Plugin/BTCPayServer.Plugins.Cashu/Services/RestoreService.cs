using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.Models;
using DotNut;
using DotNut.Abstractions;
using DotNut.Api;

namespace BTCPayServer.Plugins.Cashu;

public class RestoreService : IHostedService
{
    private readonly ILogger<RestoreService> _logger;
    private readonly SemaphoreSlim _semaphore = new (1, 1);
    private readonly ConcurrentDictionary<string, RestoreStatus> _restoreStatuses = new();
    private readonly ConcurrentQueue<RestoreJob> _restoreQueue = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _processingTask;
    private CashuDbContextFactory _dbContextFactory;

    public RestoreService(ILogger<RestoreService> logger, CashuDbContextFactory dbContextFactory)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Cashu] Restore Service starting...");
        
        _cancellationTokenSource = new CancellationTokenSource();
        _processingTask = ProcessRestoreQueueAsync(_cancellationTokenSource.Token);
        
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Cashu] Restore Service stopping...");
        
        _cancellationTokenSource?.Cancel();
        
        if (_processingTask != null)
        {
            await Task.WhenAny(_processingTask, Task.Delay(Timeout.Infinite, cancellationToken));
        }
        
        _semaphore.Dispose();
    }

    /// <summary>
    /// Add new restore task to queue
    /// </summary>
    public string QueueRestore(string storeId, List<string> mintUrls, string seed)
    {
        var jobId = Guid.NewGuid().ToString();
        var job = new RestoreJob
        {
            JobId = jobId,
            StoreId = storeId,
            MintUrls = mintUrls,
            Seed = seed,
            QueuedAt = DateTime.UtcNow
        };
        
        _restoreQueue.Enqueue(job);
        
        _restoreStatuses[jobId] = new RestoreStatus
        {
            JobId = jobId,
            StoreId = storeId,
            Status = RestoreState.Queued,
            TotalMints = mintUrls.Count,
            ProcessedMints = 0,
            StartedAt = null,
            CompletedAt = null,
            Errors = new List<string>()
        };
        
        _logger.LogInformation($"[Cashu] Restore job {jobId} queued for store {storeId} with {mintUrls.Count} mints");
        
        return jobId;
    }

    public string QueueRestore(string storeId, string mintUrls, string seed)
    {
        var mints = mintUrls.Split(" ").ToList();
        return this.QueueRestore(storeId, mints, seed);
    }

    /// <summary>
    /// Get restore status
    /// </summary>
    public RestoreStatus? GetRestoreStatus(string jobId)
    {
        return _restoreStatuses.TryGetValue(jobId, out var status) ? status : null;
    }

    /// <summary>
    /// Get restore statuses per store (for every mint)
    /// </summary>
    public List<RestoreStatus> GetStoreRestoreStatuses(string storeId)
    {
        return _restoreStatuses.Values
            .Where(s => s.StoreId == storeId)
            .OrderByDescending(s => s.QueuedAt)
            .ToList();
    }

    private async Task ProcessRestoreQueueAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_restoreQueue.TryDequeue(out var job))
                {
                    await ProcessRestoreJobAsync(job, cancellationToken);
                }
                else
                {
                    // wait for new jobs
                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Cashu] Error processing restore queue");
                await Task.Delay(5000, cancellationToken);
            }
        }
    }

    private async Task ProcessRestoreJobAsync(RestoreJob job, CancellationToken cancellationToken)
    {
        var status = _restoreStatuses[job.JobId];
        status.Status = RestoreState.Processing;
        status.StartedAt = DateTime.UtcNow;
        
        _logger.LogInformation($"Starting restore job {job.JobId} for store {job.StoreId}");

        try
        {
            foreach (var mintUrl in job.MintUrls)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    status.Status = RestoreState.Cancelled;
                    return;
                }

                status.CurrentMint = mintUrl;
                try
                {
                    await _semaphore.WaitAsync(cancellationToken);
                    
                    try
                    {
                        _logger.LogInformation($"Restoring from mint {mintUrl} for store {job.StoreId}");
                        
                        var restoredProofs = await RestoreFromMintAsync(job.StoreId, mintUrl, job.Seed, cancellationToken);
                        if (restoredProofs.Count != 0)
                        {
                            await SaveRecoveredTokensAsync(job.StoreId, restoredProofs, cancellationToken);
                            status.TotalRecoveredProofs += restoredProofs.Count;
                        }
                        
                        status.ProcessedMints++;
                        _logger.LogInformation($"Recovered {restoredProofs.Count} proofs from {mintUrl}");
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                    
                    await Task.Delay(2000, cancellationToken);
                }
                catch (Exception ex)
                {
                    var error = $"Error restoring from {mintUrl}: {ex.Message}";
                    status.Errors.Add(error);
                    _logger.LogError(ex, error);
                }
            }
            
            status.Status = status.Errors.Any() ? RestoreState.CompletedWithErrors : RestoreState.Completed;
            status.CompletedAt = DateTime.UtcNow;
            
            _logger.LogInformation(
                $"Restore job {job.JobId} completed. Recovered {status.TotalRecoveredProofs} proofs " +
                $"with {status.Errors.Count} errors");
        }
        catch (Exception ex)
        {
            status.Status = RestoreState.Failed;
            status.Errors.Add($"Fatal error: {ex.Message}");
            status.CompletedAt = DateTime.UtcNow;
            
            _logger.LogError(ex, $"Restore job {job.JobId} failed");
        }
    }

    private async Task<List<Proof>> RestoreFromMintAsync(
        string storeId,
        string mintUrl, 
        string seed, 
        CancellationToken ct)
    {
        using var httpClient = new HttpClient(){BaseAddress = new Uri(mintUrl)};
        var mint = new CashuHttpClient(httpClient);
        
        var counter = new DbCounter(_dbContextFactory, storeId);
        var wallet = Wallet.Create()
            .WithMint(mint)
            .WithMnemonic(seed)
            .WithCounter(counter);

       var proofs=  await wallet
           .Restore()
           .ProcessAsync(ct);
       return proofs.ToList();
    }

    private async Task SaveRecoveredTokensAsync(
        string storeId, 
        List<Proof> proofs, 
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = _dbContextFactory.CreateContext();
            db.Proofs.AddRange(StoredProof.FromBatch(proofs, storeId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Cashu] Error saving recovered tokens to db!");
        }
    }
}

public class RestoreJob
{
    public string JobId { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
    public List<string> MintUrls { get; set; } = new();
    public string Seed { get; set; } = string.Empty;
    public DateTime QueuedAt { get; set; }
}

public class RestoreStatus
{
    public string JobId { get; set; } = string.Empty;
    public string StoreId { get; set; }
    public RestoreState Status { get; set; }
    public int TotalMints { get; set; }
    public int ProcessedMints { get; set; }
    public string? CurrentMint { get; set; }
    public List<string> UnreachableMints { get; set; }
    public int TotalRecoveredProofs { get; set; }
    public List<string> Errors { get; set; } = new();
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public enum RestoreState
{
    Queued,
    Processing,
    Completed,
    CompletedWithErrors,
    Failed,
    Cancelled
}