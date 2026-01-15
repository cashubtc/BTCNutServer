#nullable enable

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
using DotNut.NBitcoin.BIP39;

namespace BTCPayServer.Plugins.Cashu.Services;

public class RestoreService : IHostedService
{
    private readonly ILogger<RestoreService> _logger;
    private readonly SemaphoreSlim _semaphore = new (1, 1);
    private readonly ConcurrentDictionary<string, RestoreStatus> _restoreStatuses = new();
    private readonly ConcurrentQueue<RestoreJob> _restoreQueue = new();
    private readonly CashuDbContextFactory _dbContextFactory;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _processingTask;

    public RestoreService(ILogger<RestoreService> logger, CashuDbContextFactory dbContextFactory)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Cashu] Wallet restore Service starting...");
        
        _cancellationTokenSource = new CancellationTokenSource();
        _processingTask = ProcessRestoreQueueAsync(_cancellationTokenSource.Token);
        
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Cashu] Wallet restore Service stopping...");
        
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
            Errors = new List<string>(),
            RestoredMints = new List<RestoredMint>()
        };
        
        _logger.LogInformation($"[Cashu] Wallet restore job {jobId} queued for store {storeId} with {mintUrls.Count} mints");
        
        return jobId;
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
                _logger.LogError(ex, "[Cashu] Error processing wallet restore queue");
                await Task.Delay(5000, cancellationToken);
            }
        }
    }

    private async Task ProcessRestoreJobAsync(RestoreJob job, CancellationToken cancellationToken)
    {
        var status = _restoreStatuses[job.JobId];
        status.Status = RestoreState.Processing;
        status.StartedAt = DateTime.UtcNow;
        
        _logger.LogInformation($"[Cashu] Starting wallet restore job {job.JobId} for store {job.StoreId}");

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
                        
                        var restoredMint = await RestoreFromMintAsync(job.StoreId, mintUrl, job.Seed, cancellationToken);
                        if (restoredMint.Proofs.Count != 0)
                        {
                            await SaveRecoveredTokensAsync(job.StoreId, mintUrl, restoredMint.Proofs, cancellationToken);
                        }
                        status.RestoredMints.Add(restoredMint);
                        status.ProcessedMints++;
                        _logger.LogInformation($"Recovered {restoredMint.Proofs.Count} proofs from {mintUrl}");
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
                    status.UnreachableMints.Add(mintUrl);
                    status.Errors.Add(error);
                    _logger.LogError(ex, error);
                }
            }
            
            status.Status = status.Errors.Any() ? RestoreState.CompletedWithErrors : RestoreState.Completed;
            status.CompletedAt = DateTime.UtcNow;
            await SaveWalletConfig(job.StoreId, new Mnemonic(job.Seed), cancellationToken);
            
            _logger.LogInformation(
                $"Restore job {job.JobId} completed." + (status.Errors.Any() ? $"with {status.Errors.Count} errors": ""));
        }
        catch (Exception ex)
        {
            status.Status = RestoreState.Failed;
            status.Errors.Add($"Fatal error: {ex.Message}");
            status.CompletedAt = DateTime.UtcNow;
            
            _logger.LogError(ex, $"Restore job {job.JobId} failed");
        }
    }

    private async Task<RestoredMint> RestoreFromMintAsync(
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

       var proofList = proofs.ToList();

       var keysetUnits = await wallet.GetActiveKeysetIdsWithUnits(ct);
       var amountsPerUnit = new Dictionary<string, ulong>();
       
       if (keysetUnits != null)
       {
           foreach (var keyValuePair in keysetUnits)
           {
               var key = keyValuePair.Key;
               
               amountsPerUnit[key] = amountsPerUnit.GetValueOrDefault(key) + proofList
                                         .Where(p => p.Id == keyValuePair.Value)
                                         .Select(p => p.Amount)
                                         .Sum();
           }
       }
       
        return new RestoredMint()
        {
            MintUrl = mintUrl,
            Proofs = proofList,
            Balances = amountsPerUnit
        };
    }

    private async Task SaveRecoveredTokensAsync(
        string storeId, 
        string mintUrl,
        List<Proof> proofs, 
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = _dbContextFactory.CreateContext();
            
            // add mint to db if not present.
            if (!db.Mints.Any(m => m.Url == mintUrl))
            {
                db.Mints.Add(new Mint(mintUrl));
            }
            // add proofs
            db.Proofs.AddRange(StoredProof.FromBatch(proofs, storeId));
            
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Cashu] Error saving recovered tokens to db!");
        }
    }

    private async Task SaveWalletConfig(string storeId, Mnemonic mnemonic, CancellationToken ct)
    {
        try
        {
            await using var db = _dbContextFactory.CreateContext();
            db.CashuWalletConfig.Add(new CashuWalletConfig()
            {
                StoreId = storeId,
                WalletMnemonic = mnemonic,
                Verified = true,
            });
            await db.SaveChangesAsync(ct);
            // counter is already set and if anything happens, will be overriden while restore process.

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Cashu] Error saving recovered seed to db!");
            
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
    public List<string> UnreachableMints { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<RestoredMint> RestoredMints { get; set; }
    
}

public class RestoredMint
{
    public string MintUrl { get; set; } = string.Empty;
    public List<Proof> Proofs { get; set; }
    public Dictionary<string, ulong> Balances { get; set; }
}
