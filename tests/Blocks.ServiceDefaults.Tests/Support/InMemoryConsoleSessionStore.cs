using Blocks.ServiceDefaults.Clients;
using Blocks.ServiceDefaults.Sessions;

namespace Blocks.ServiceDefaults.Tests.Support;

/// <summary>
/// <see cref="IConsoleSessionStore"/> in memory, with the same rules as the EF store. One instance can be
/// shared by several hosts to stand for one database behind several instances. <see cref="Unavailable"/>
/// simulates an outage.
/// </summary>
public sealed class InMemoryConsoleSessionStore : IConsoleSessionStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, ConsoleSession> _sessions = new(StringComparer.Ordinal);

    /// <summary>While true, every call throws <see cref="StorageUnavailableException"/>.</summary>
    public bool Unavailable { get; set; }

    public IReadOnlyCollection<ConsoleSession> Sessions
    {
        get
        {
            lock (_lock)
                return [.. _sessions.Values];
        }
    }

    public Task CreateAsync(ConsoleSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfUnavailable();
        lock (_lock)
            _sessions.Add(Convert.ToHexString(session.IdHash), session);
        return Task.CompletedTask;
    }

    public Task<ConsoleSession?> FindAsync(byte[] idHash, CancellationToken ct)
    {
        ThrowIfUnavailable();
        lock (_lock)
            return Task.FromResult(_sessions.GetValueOrDefault(Convert.ToHexString(idHash)));
    }

    public Task RevokeAsync(byte[] idHash, DateTimeOffset at, CancellationToken ct)
    {
        ThrowIfUnavailable();
        var id = Convert.ToHexString(idHash);
        lock (_lock)
        {
            if (_sessions.TryGetValue(id, out var session) && session.RevokedAt is null)
                _sessions[id] = session with { RevokedAt = at };
        }

        return Task.CompletedTask;
    }

    public Task DeleteExpiredBeforeAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        ThrowIfUnavailable();
        lock (_lock)
        {
            foreach (var (id, session) in _sessions.ToList())
            {
                if (session.ExpiresAt < cutoff)
                    _sessions.Remove(id);
            }
        }

        return Task.CompletedTask;
    }

    private void ThrowIfUnavailable()
    {
        if (Unavailable)
            throw new StorageUnavailableException();
    }
}
