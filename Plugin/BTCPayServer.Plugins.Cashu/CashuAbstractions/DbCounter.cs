using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.Models;
using DotNut;
using DotNut.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Cashu.CashuAbstractions;

public class DbCounter : ICounter
{
    private readonly CashuDbContextFactory _dbContextFactory;
    private readonly string _storeId;

    public DbCounter(CashuDbContextFactory dbContextFactory, string storeId)
    {
        _dbContextFactory = dbContextFactory;
        _storeId = storeId;
    }

    public async Task<uint> GetCounterForId(KeysetId keysetId, CancellationToken ct = default)
    {
        await using var db = _dbContextFactory.CreateContext();
        var entry = await db.StoreKeysetCounters
            .FirstOrDefaultAsync(c => c.StoreId == _storeId && c.KeysetId == keysetId, ct);

        return entry?.Counter ?? 0;
    }

    public async Task<uint> IncrementCounter(KeysetId keysetId, uint bumpBy = 1, CancellationToken ct = default)
    {
        await using var db = _dbContextFactory.CreateContext();
        var entry = await db.StoreKeysetCounters
            .FirstOrDefaultAsync(c => c.StoreId == _storeId && c.KeysetId == keysetId, ct);

        uint newValue;
        if (entry == null)
        {
            newValue = bumpBy;
            db.StoreKeysetCounters.Add(new StoreKeysetCounter
            {
                StoreId = _storeId,
                KeysetId = keysetId,
                Counter = newValue
            });
        }
        else
        {
            entry.Counter += bumpBy;
            newValue = entry.Counter;
        }

        await db.SaveChangesAsync(ct);
        return newValue;
    }

    public async Task SetCounter(KeysetId keysetId, uint counter, CancellationToken ct = default)
    {
        await using var db = _dbContextFactory.CreateContext();
        var entry = await db.StoreKeysetCounters
            .FirstOrDefaultAsync(c => c.StoreId == _storeId && c.KeysetId == keysetId, ct);

        if (entry == null)
        {
            db.StoreKeysetCounters.Add(new StoreKeysetCounter
            {
                StoreId = _storeId,
                KeysetId = keysetId,
                Counter = counter
            });
        }
        else
        {
            entry.Counter = counter;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<KeysetId, uint>> Export()
    {
        await using var db = _dbContextFactory.CreateContext();
        var counters = await db.StoreKeysetCounters
            .Where(c => c.StoreId == _storeId)
            .ToListAsync();

        return counters.ToDictionary(c => c.KeysetId, c => c.Counter);
    }
}
