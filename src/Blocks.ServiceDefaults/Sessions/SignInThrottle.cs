using System.Collections.Concurrent;
using System.Net;

namespace Blocks.ServiceDefaults.Sessions;

/// <summary>
/// Counts failed sign-ins per address in fixed one-minute windows and refuses an address that has
/// failed <see cref="MaxFailures"/> times in the current one. Successes are not counted.
/// </summary>
/// <remarks>
/// In memory, per instance: with N instances the effective limit is 5·N a minute. The admin key is
/// 238 random bits, so this limits log noise and scanners; it is not what makes guessing infeasible.
/// </remarks>
internal sealed class SignInThrottle(TimeProvider clock)
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    // Pruned lazily; past this size, windows that have ended are swept on the next failure.
    private const int PruneThreshold = 1024;

    private readonly ConcurrentDictionary<IPAddress, Counter> _counters = new();

    /// <summary>False, with how long to wait, when the address has used up its failures for this window.</summary>
    public bool TryEnter(IPAddress address, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!_counters.TryGetValue(address, out var counter))
            return true;

        var now = clock.GetUtcNow();
        lock (counter)
        {
            if (now >= counter.WindowEnds || counter.Failures < MaxFailures)
                return true;

            retryAfter = counter.WindowEnds - now;
            return false;
        }
    }

    public void RecordFailure(IPAddress address)
    {
        var now = clock.GetUtcNow();
        if (_counters.Count > PruneThreshold)
            Prune(now);

        var counter = _counters.GetOrAdd(address, _ => new Counter { WindowEnds = now + Window });
        lock (counter)
        {
            if (now >= counter.WindowEnds)
            {
                counter.WindowEnds = now + Window;
                counter.Failures = 0;
            }

            counter.Failures++;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (address, counter) in _counters)
        {
            if (now >= counter.WindowEnds)
                _counters.TryRemove(address, out _);
        }
    }

    private sealed class Counter
    {
        public DateTimeOffset WindowEnds { get; set; }
        public int Failures { get; set; }
    }
}
