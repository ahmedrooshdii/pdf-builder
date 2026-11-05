using Blocks.ServiceDefaults.Sessions;

namespace Blocks.ServiceDefaults.EntityFramework;

/// <summary>
/// A row of <c>console_sessions</c>: one operator sign-in. Holds no client data, so no <c>client_id</c>.
/// </summary>
internal sealed class ConsoleSessionRecord
{
    /// <summary>SHA-256 of the session id; the id itself is only inside the protected cookie.</summary>
    public byte[] IdHash { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set by sign-out; never cleared.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>16 bytes identifying the admin key the session was made with.</summary>
    public byte[] AdminKeyFingerprint { get; set; } = [];

    public ConsoleSession ToSession() => new(IdHash, CreatedAt, ExpiresAt, RevokedAt, AdminKeyFingerprint);

    public static ConsoleSessionRecord From(ConsoleSession session) => new()
    {
        IdHash = session.IdHash,
        CreatedAt = session.CreatedAt.ToUniversalTime(),
        ExpiresAt = session.ExpiresAt.ToUniversalTime(),
        RevokedAt = session.RevokedAt?.ToUniversalTime(),
        AdminKeyFingerprint = session.AdminKeyFingerprint
    };
}
