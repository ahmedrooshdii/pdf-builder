using System.Net;
using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Clients;
using Blocks.ServiceDefaults.Tests.Support;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>
/// Story 4 and FR-013: what an instance knows about a key is at most a minute old, so revocation,
/// disabling and enabling reach every instance within 60 seconds, at one read per key per minute.
/// </summary>
public sealed class KeyCacheTests
{
    private static readonly string[] Generate = ["pdf:generate"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static KeyLookup Lookup(string keyId) => new(
        new ApiKeyInfo(keyId, "ecommerce", "k", Generate, new byte[32], DateTimeOffset.UnixEpoch, null, null, null),
        ClientDisabled: false);

    [Fact]
    public async Task A_key_is_read_once_a_minute_and_a_missing_key_is_remembered_too()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        using var cache = new KeyCache(clock);
        var reads = new Dictionary<string, int>();
        Task<KeyLookup?> Load(string keyId, CancellationToken _)
        {
            reads[keyId] = reads.GetValueOrDefault(keyId) + 1;
            return Task.FromResult(keyId == "known000" ? Lookup(keyId) : null);
        }

        for (var i = 0; i < 10; i++)
        {
            Assert.NotNull(await cache.GetAsync("known000", Load, Ct));
            Assert.Null(await cache.GetAsync("missing0", Load, Ct));
            clock.Advance(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(1, reads["known000"]);
        Assert.Equal(1, reads["missing0"]);

        clock.Advance(TimeSpan.FromSeconds(10));     // 60 s since the first read
        await cache.GetAsync("known000", Load, Ct);
        await cache.GetAsync("missing0", Load, Ct);

        Assert.Equal(2, reads["known000"]);
        Assert.Equal(2, reads["missing0"]);
    }

    /// <summary>SC-008: a burst of invented keys cannot take every database connection at once.</summary>
    [Fact]
    public async Task No_more_than_a_few_lookups_reach_the_store_at_once()
    {
        using var cache = new KeyCache(new ManualClock(DateTimeOffset.UnixEpoch));
        var running = 0;
        var peak = 0;
        async Task<KeyLookup?> Load(string _, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            await Task.Delay(5, ct);
            Interlocked.Decrement(ref running);
            return null;
        }

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => cache.GetAsync($"{i:D8}", Load, Ct)));

        Assert.InRange(peak, 1, KeyCache.MaxConcurrentLoads);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value
               && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    [Fact]
    public async Task A_storage_failure_is_not_remembered()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        using var cache = new KeyCache(clock);
        var fail = true;

        await Assert.ThrowsAsync<StorageUnavailableException>(() => cache.GetAsync(
            "known000", (_, _) => fail ? throw new StorageUnavailableException() : Task.FromResult<KeyLookup?>(null), Ct));

        fail = false;
        Assert.NotNull(await cache.GetAsync("known000", (id, _) => Task.FromResult<KeyLookup?>(Lookup(id)), Ct));
    }

    [Fact]
    public async Task Forgetting_a_key_makes_the_next_request_read_it_again()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        using var cache = new KeyCache(clock);
        var reads = 0;
        Task<KeyLookup?> Load(string id, CancellationToken _)
        {
            reads++;
            return Task.FromResult<KeyLookup?>(Lookup(id));
        }

        await cache.GetAsync("known000", Load, Ct);
        cache.Forget("known000");
        await cache.GetAsync("known000", Load, Ct);

        Assert.Equal(2, reads);
    }

    /// <summary>Two instances over one store: a change made through one reaches the other within a minute.</summary>
    [Fact]
    public async Task Another_instance_refuses_a_revoked_key_within_a_minute_and_not_before_its_cache_expires()
    {
        var store = new InMemoryClientKeyStore();
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        await using var first = await StudHost.StartAsync(store, clock);
        await using var second = await StudHost.StartAsync(store, clock);
        await first.RegisterAsync("ecommerce", Ct);
        var issued = await first.IssueAsync("ecommerce", Generate, Ct);
        var key = issued.GetProperty("key").GetString();

        using var onSecond = second.Client(key);
        Assert.Equal(HttpStatusCode.OK, (await onSecond.GetAsync("/generate", Ct)).StatusCode);

        await first.Admin().DeleteAsync($"/api/admin/clients/ecommerce/keys/{issued.GetProperty("keyId").GetString()}", Ct);

        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(HttpStatusCode.OK, (await onSecond.GetAsync("/generate", Ct)).StatusCode);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await onSecond.GetAsync("/generate", Ct)).StatusCode);
    }

    [Fact]
    public async Task Another_instance_sees_a_client_disabled_and_enabled_within_a_minute()
    {
        var store = new InMemoryClientKeyStore();
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        await using var first = await StudHost.StartAsync(store, clock);
        await using var second = await StudHost.StartAsync(store, clock);
        var key = store.Seed("ecommerce", Generate, clock.Now);
        using var onSecond = second.Client(key);
        await onSecond.GetAsync("/generate", Ct);

        await first.Admin().PatchAsync("/api/admin/clients/ecommerce", JsonBody("""{ "disabled": true }"""), Ct);
        clock.Advance(KeyCache.Lifetime);
        Assert.Equal(HttpStatusCode.Forbidden, (await onSecond.GetAsync("/generate", Ct)).StatusCode);

        await first.Admin().PatchAsync("/api/admin/clients/ecommerce", JsonBody("""{ "disabled": false }"""), Ct);
        clock.Advance(KeyCache.Lifetime);
        Assert.Equal(HttpStatusCode.OK, (await onSecond.GetAsync("/generate", Ct)).StatusCode);
    }

    [Fact]
    public async Task Expiry_is_exact_even_for_a_cached_key()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        await using var host = await StudHost.StartAsync(clock: clock);
        var key = host.Store.Seed("ecommerce", Generate, clock.Now, expiresAt: clock.Now.AddSeconds(10));
        using var client = host.Client(key);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/generate", Ct)).StatusCode);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/generate", Ct)).StatusCode);
        Assert.Equal(1, host.Store.Reads);
    }

    private static StringContent JsonBody(string json) => new(json, System.Text.Encoding.UTF8, "application/json");
}
