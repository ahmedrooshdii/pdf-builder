using System.Collections.Concurrent;
using System.Threading.Channels;
using Blocks.ServiceDefaults.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Blocks.ServiceDefaults.Authentication;

/// <summary>
/// Notes that a key was used, without making the request wait for a write: at most once a minute per
/// key per instance, the key id goes on a queue that <see cref="KeyUsageFlushService"/> drains.
/// </summary>
internal sealed class KeyUsageRecorder(TimeProvider clock)
{
    /// <summary>The resolution of <c>last_used_at</c>; a key used more often costs no more writes.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    // Bounded, and full means "drop": a backlog of usage times is never worth slowing a request for.
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(10_000)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true
    });

    // One entry per key that has authenticated on this instance: real keys only, never invented ones.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastQueued = new(StringComparer.Ordinal);

    public ChannelReader<string> Pending => _queue.Reader;

    public void Record(string keyId)
    {
        var now = clock.GetUtcNow();

        if (_lastQueued.TryGetValue(keyId, out var last) && now - last < Interval && now >= last)
            return;

        _lastQueued[keyId] = now;
        _queue.Writer.TryWrite(keyId);
    }
}

/// <summary>Writes the queued usage times every few seconds, one update for all the keys in a batch.</summary>
internal sealed class KeyUsageFlushService(
    KeyUsageRecorder recorder,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<KeyUsageFlushService> logger) : BackgroundService
{
    /// <summary>How long uses are gathered before they are written.</summary>
    public static readonly TimeSpan BatchDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await recorder.Pending.WaitToReadAsync(stoppingToken))
            {
                await Task.Delay(BatchDelay, stoppingToken);
                await FlushAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: what is still queued is at most a few seconds of usage times.
        }
    }

    internal async Task FlushAsync(CancellationToken ct)
    {
        var keyIds = new HashSet<string>(StringComparer.Ordinal);
        while (recorder.Pending.TryRead(out var keyId))
            keyIds.Add(keyId);

        if (keyIds.Count == 0)
            return;

        var now = clock.GetUtcNow();
        var minute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IClientKeyStore>();
            await store.RecordLastUsedAsync(keyIds, minute, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A usage time is housekeeping: losing one batch must not stop the next.
            AuthLog.UsageNotRecorded(logger, keyIds.Count, exception);
        }
    }
}
