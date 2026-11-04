using Blocks.ServiceDefaults.Clients;

namespace Blocks.ServiceDefaults.EntityFramework;

/// <summary>
/// A row of <c>api_keys</c>. The secret is not here, only its SHA-256; whether a key is active, expired
/// or revoked is derived from the dates and never stored.
/// </summary>
internal sealed class ApiKeyRecord
{
    public string KeyId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = [];
    public byte[] SecretHash { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Set once, never cleared.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public static ApiKeyRecord From(ApiKeyInfo key) => new()
    {
        KeyId = key.KeyId,
        ClientId = key.ClientId,
        Name = key.Name,
        Scopes = [.. key.Scopes],
        SecretHash = key.SecretHash.ToArray(),
        CreatedAt = key.CreatedAt.ToUniversalTime(),
        ExpiresAt = key.ExpiresAt?.ToUniversalTime(),
        RevokedAt = key.RevokedAt?.ToUniversalTime(),
        LastUsedAt = key.LastUsedAt?.ToUniversalTime()
    };

    public ApiKeyInfo ToInfo() =>
        new(KeyId, ClientId, Name, Scopes, SecretHash, CreatedAt, ExpiresAt, RevokedAt, LastUsedAt);
}
