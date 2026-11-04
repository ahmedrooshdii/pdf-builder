namespace Blocks.ServiceDefaults.Clients;

/// <summary>
/// Where clients and keys are kept. <c>Blocks.ServiceDefaults.EntityFramework</c> implements it over
/// the block's own database.
/// </summary>
/// <remarks>
/// An implementation throws <see cref="StorageUnavailableException"/> when the storage cannot be
/// reached and a retry may succeed, so that an outage is answered "try again" and never "invalid key".
/// </remarks>
public interface IClientKeyStore
{
    /// <summary>The key with this id together with its client's state, or null when there is none.</summary>
    Task<KeyLookup?> FindKeyAsync(string keyId, CancellationToken ct);

    /// <summary>Registers a client. False when the id is taken, by an active or a disabled client.</summary>
    Task<bool> AddClientAsync(ClientInfo client, CancellationToken ct);

    Task<ClientInfo?> GetClientAsync(string clientId, CancellationToken ct);

    /// <summary>Every client, ordered by id.</summary>
    Task<IReadOnlyList<ClientInfo>> ListClientsAsync(CancellationToken ct);

    /// <summary>
    /// Renames, disables or enables a client; a null argument leaves that part alone, and disabling a
    /// disabled client changes nothing. Null when the client does not exist.
    /// </summary>
    Task<ClientInfo?> UpdateClientAsync(string clientId, string? name, bool? disabled, DateTimeOffset now, CancellationToken ct);

    /// <summary>Stores a new key. False when its key id is already used, so the caller draws a new one.</summary>
    Task<bool> AddKeyAsync(ApiKeyInfo key, CancellationToken ct);

    /// <summary>Every key of the client, newest first.</summary>
    Task<IReadOnlyList<ApiKeyInfo>> ListKeysAsync(string clientId, CancellationToken ct);

    /// <summary>
    /// Revokes a key of this client, once: revoking it again leaves the first revocation time. Null
    /// when the client has no key with that id.
    /// </summary>
    Task<ApiKeyInfo?> RevokeKeyAsync(string clientId, string keyId, DateTimeOffset now, CancellationToken ct);

    /// <summary>Sets the last-used time of each key, in one write.</summary>
    Task RecordLastUsedAsync(IReadOnlyCollection<string> keyIds, DateTimeOffset usedAt, CancellationToken ct);
}

/// <summary>A client: a product as the service sees it.</summary>
public sealed record ClientInfo(string Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? DisabledAt)
{
    public bool IsDisabled => DisabledAt is not null;
}

/// <summary>A key as stored: never the key itself, only the hash of its secret.</summary>
public sealed record ApiKeyInfo(
    string KeyId,
    string ClientId,
    string Name,
    IReadOnlyList<string> Scopes,
    ReadOnlyMemory<byte> SecretHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt)
{
    /// <summary><c>Revoked</c> wins over <c>Expired</c>; a key is never stored in this state, it is derived.</summary>
    public ApiKeyStatus StatusAt(DateTimeOffset now) =>
        RevokedAt is not null ? ApiKeyStatus.Revoked
        : ExpiresAt is { } expires && expires <= now ? ApiKeyStatus.Expired
        : ApiKeyStatus.Active;
}

public enum ApiKeyStatus
{
    Active,
    Expired,
    Revoked
}

/// <summary>What authentication needs about a key: the key and whether its client is disabled.</summary>
public sealed record KeyLookup(ApiKeyInfo Key, bool ClientDisabled);
