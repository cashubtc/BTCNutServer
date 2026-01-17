using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.Models;
using DotNut;
using DotNut.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Cashu.CashuAbstractions;

public class DbCounter : ICounter
{
    private CashuDbContextFactory _dbContextFactory = null;
    private string _storeId;
    
    public DbCounter(CashuDbContextFactory cashuDbContextFactory, string storeId)
    {
        this._dbContextFactory = cashuDbContextFactory;
        this._storeId = storeId;
    }
    
    public async Task<uint> GetCounterForId(KeysetId keysetId, CancellationToken ct = default)
    {
        await using var db = _dbContextFactory.CreateContext();
        var counter = await db.StoreKeysetCounters
            .SingleOrDefaultAsync(c => c.KeysetId == keysetId && c.StoreId == _storeId, ct );
        if (counter.StoreId == null)
        {
            return 0;
        }

        return counter.Counter;
    }

    public async Task<uint> IncrementCounter(KeysetId keysetId, uint bumpBy = 1, CancellationToken ct = default)
    {
        await using var db = _dbContextFactory.CreateContext();
        var counter = await db.StoreKeysetCounters
            .SingleOrDefaultAsync(c => c.KeysetId == keysetId && c.StoreId == _storeId, ct);
        
        if (counter == null)
        {
            counter = new StoreKeysetCounter
            {
                StoreId = _storeId,
                KeysetId = keysetId,
                Counter = 0
            };
            db.StoreKeysetCounters.Add(counter);
        }
        
        counter.Counter += bumpBy;
        await db.SaveChangesAsync(ct);
        return counter.Counter;
    }

    public async Task SetCounter(KeysetId keysetId, uint value, CancellationToken ct)
    {
        await using var db = _dbContextFactory.CreateContext();
        var counter = await db.StoreKeysetCounters
            .SingleOrDefaultAsync(c => c.KeysetId == keysetId && c.StoreId == _storeId, ct);
        
        if (counter == null)
        {
            counter = new StoreKeysetCounter
            {
                StoreId = _storeId,
                KeysetId = keysetId,
                Counter = 0
            };
            db.StoreKeysetCounters.Add(counter);
        }

        counter.Counter = value;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<KeysetId, uint>> Export()
    {
        await using var db = _dbContextFactory.CreateContext();
        return await db.StoreKeysetCounters
            .Where(c => c.StoreId == _storeId)
            .ToDictionaryAsync(c => c.KeysetId, c => c.Counter);
    }

    public static ICounter GetCounterForStore(IServiceProvider serviceProvider, string storeId)
    {
        var dbContextFactory = serviceProvider.GetRequiredService<CashuDbContextFactory>();
        return new DbCounter(dbContextFactory, storeId);
    }
}