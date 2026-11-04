using Blocks.ServiceDefaults.Clients;
using Microsoft.Extensions.Caching.Memory;

namespace Blocks.ServiceDefaults.Authentication;

/// <summary>
/// What this instance last read about each key id, kept for one minute. Revoking a key or disabling
/// its client is therefore seen by every instance within <see cref="Lifetime"/>, while most requests
/// cost no database read at all.
/// </summary>
/// <remarks>
/// <para>
/// "No such key" is cached too, so a scan of invented key ids costs one read per id per minute rather
/// than one per request. A storage failure is never cached: the next request tries again.
/// </para>
/// <para>
/// Age is measured with <see cref="TimeProvider"/>, so tests can move the clock; the memory cache's
/// own expiry only evicts. The cache is private and bounded, so a flood of invented ids can neither
/// grow memory without limit nor push out entries other components keep in the shared cache.
/// </para>
/// </remarks>
internal sealed class KeyCache(TimeProvider clock) : IDisposable
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Lookups that may reach the database at once. A burst of invented keys all misses the cache; left
    /// alone it would take every pooled connection and starve the requests of valid clients.
    /// </summary>
    public const int MaxConcurrentLoads = 8;

    private const int MaxEntries = 100_000;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxEntries });
    private readonly SemaphoreSlim _loads = new(MaxConcurrentLoads, MaxConcurrentLoads);

    public async Task<KeyLookup?> GetAsync(
        string keyId, Func<string, CancellationToken, Task<KeyLookup?>> load, CancellationToken ct)
    {
        if (TryGetFresh(keyId, out var cached))
            return cached;

        KeyLookup? lookup;
        await _loads.WaitAsync(ct);
        try
        {
            // Another request may have read this key while this one waited.
            if (TryGetFresh(keyId, out cached))
                return cached;

            lookup = await load(keyId, ct);
        }
        finally
        {
            _loads.Release();
        }

        var now = clock.GetUtcNow();

        _cache.Set(keyId, new Entry(lookup, now), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Lifetime,
            Size = 1
        });

        return lookup;
    }

    private bool TryGetFresh(string keyId, out KeyLookup? lookup)
    {
        var now = clock.GetUtcNow();

        if (_cache.TryGetValue(keyId, out Entry? entry) && entry is not null
            && entry.ReadAt <= now && now - entry.ReadAt < Lifetime)
        {
            lookup = entry.Lookup;
            return true;
        }

        lookup = null;
        return false;
    }

    /// <summary>
    /// Drops what this instance knows about a key, after a change made through this instance. Other
    /// instances still see the change within <see cref="Lifetime"/>.
    /// </summary>
    public void Forget(string keyId) => _cache.Remove(keyId);

    public void Dispose()
    {
        _cache.Dispose();
        _loads.Dispose();
    }

    private sealed record Entry(KeyLookup? Lookup, DateTimeOffset ReadAt);
}
