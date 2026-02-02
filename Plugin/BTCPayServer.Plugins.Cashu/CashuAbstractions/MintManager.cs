using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.Models;
using DotNut;
using DotNut.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Cashu.CashuAbstractions;

public class MintManager
{
    private readonly CashuDbContextFactory _dbContextFactory;

    public MintManager(CashuDbContextFactory dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<Mint> GetOrCreateMint(string mintUrl)
    {
        await using var db = _dbContextFactory.CreateContext();
        
        var mint = await db.Mints.FirstOrDefaultAsync(m => m.Url == mintUrl);
        if (mint != null)
        {
            return mint;
        }

        mint = new Mint(mintUrl);
        db.Mints.Add(mint);
        await db.SaveChangesAsync();
        
        return mint;
    }


    public async Task SaveKeyset(string mintUrl, KeysetId keysetId, Keyset keyset, string unit)
    {
        await using var db = _dbContextFactory.CreateContext();
        
        var mint = await GetOrCreateMint(mintUrl);

        var existingEntry = await db.MintKeys.FirstOrDefaultAsync(mk =>
            mk.MintId == mint.Id && mk.KeysetId == keysetId
        );

        if (existingEntry != null)
        {
            return;
        }
        
        // in case of keysetID collision, don't save this one
        var keysetInOtherMint = await db.MintKeys
            .Include(mk => mk.Mint)
            .FirstOrDefaultAsync(mk => mk.KeysetId == keysetId && mk.MintId != mint.Id);
        if (keysetInOtherMint != null)
        {
            throw new InvalidOperationException(
                $"KeysetId {keysetId} already exists in another mint ({keysetInOtherMint.Mint.Url}). " +
                $"This should never happen - KeysetIds must be globally unique!"
            );
        }

        db.MintKeys.Add(
            new MintKeys
            {
                MintId = mint.Id,
                Mint = mint,
                KeysetId = keysetId,
                Unit = unit,
                Keyset = keyset,
            }
        );

        await db.SaveChangesAsync();
    }

    public async Task<(string MintUrl, string Unit)?> GetKeysetInfo(KeysetId keysetId)
    {
        await using var db = _dbContextFactory.CreateContext();
        
        var mintKey = await db.MintKeys
            .Include(mk => mk.Mint)
            .FirstOrDefaultAsync(mk => mk.KeysetId == keysetId);

        if (mintKey == null)
        {
            return null;
        }

        return (mintKey.Mint.Url, mintKey.Unit);
    }

    public async Task<Dictionary<string, (string MintUrl, string Unit)>> MapKeysetIdsToMints(IEnumerable<KeysetId> keysetIds)
    {
        await using var db = _dbContextFactory.CreateContext();
        
        var keysetIdStrings = keysetIds.Select(k => k.ToString()).ToList();
        
        var mintKeysets = await db.MintKeys
            .Include(mk => mk.Mint)
            .Where(mk => keysetIdStrings.Contains(mk.KeysetId.ToString()))
            .ToListAsync();

        return mintKeysets.ToDictionary(
            mk => mk.KeysetId.ToString(),
            mk => (MintUrl: mk.Mint.Url, Unit: mk.Unit)
        );
    }

    public async Task<bool> MintExists(string mintUrl)
    {
        await using var db = _dbContextFactory.CreateContext();
        return await db.Mints.AnyAsync(m => m.Url == mintUrl);
    }
}