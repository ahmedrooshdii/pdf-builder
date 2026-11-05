namespace Blocks.ServiceDefaults.Sessions;

/// <summary>
/// A console sign-in as the service stores it. The session id itself is only inside the protected
/// cookie; the row holds its SHA-256, so reading the database is not enough to make a cookie that works.
/// </summary>
/// <param name="IdHash">SHA-256 of the session id: 32 bytes.</param>
/// <param name="CreatedAt">When the operator signed in.</param>
/// <param name="ExpiresAt">When the session ends; never moved.</param>
/// <param name="RevokedAt">When the operator signed out, if they did.</param>
/// <param name="AdminKeyFingerprint">16 bytes identifying the admin key the session was made with.</param>
public sealed record ConsoleSession(
    byte[] IdHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    byte[] AdminKeyFingerprint);

/// <summary>
/// Where console sessions are kept. Every method throws
/// <see cref="Clients.StorageUnavailableException"/> when the storage cannot be reached.
/// </summary>
public interface IConsoleSessionStore
{
    Task CreateAsync(ConsoleSession session, CancellationToken ct);

    /// <summary>The session with this id hash, or null.</summary>
    Task<ConsoleSession?> FindAsync(byte[] idHash, CancellationToken ct);

    /// <summary>Marks the session signed out. A session already signed out keeps its first time.</summary>
    Task RevokeAsync(byte[] idHash, DateTimeOffset at, CancellationToken ct);

    /// <summary>Deletes every session that expired before <paramref name="cutoff"/>.</summary>
    Task DeleteExpiredBeforeAsync(DateTimeOffset cutoff, CancellationToken ct);
}
