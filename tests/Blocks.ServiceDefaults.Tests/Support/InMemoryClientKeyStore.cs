using Blocks.ServiceDefaults.Clients;

namespace Blocks.ServiceDefaults.Tests.Support;

/// <summary>
/// <see cref="IClientKeyStore"/> in memory, with the same rules as the EF store, so the handler and
/// the management endpoints can be tested without a database. <see cref="Unavailable"/> simulates an
/// outage, and <see cref="Reads"/> counts key lookups.
/// </summary>
public sealed class InMemoryClientKeyStore : IClientKeyStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, ClientInfo> _clients = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ApiKeyInfo> _keys = new(StringComparer.Ordinal);
    private int _reads;
    private int _clientReads;

    /// <summary>While true, every call throws <see cref="StorageUnavailableException"/>.</summary>
    public bool Unavailable { get; set; }

    /// <summary>How many times <see cref="FindKeyAsync"/> reached the store.</summary>
    public int Reads => Volatile.Read(ref _reads);

    /// <summary>How many times <see cref="GetClientAsync"/> reached the store.</summary>
    public int ClientReads => Volatile.Read(ref _clientReads);

    public IReadOnlyCollection<ApiKeyInfo> Keys
    {
        get
        {
            lock (_lock)
                return [.. _keys.Values];
        }
    }

    public Task<KeyLookup?> FindKeyAsync(string keyId, CancellationToken ct)
    {
        Interlocked.Increment(ref _reads);
        ThrowIfUnavailable();

        lock (_lock)
        {
            return Task.FromResult(_keys.TryGetValue(keyId, out var key)
                ? new KeyLookup(key, _clients[key.ClientId].IsDisabled)
                : null);
        }
    }

    public Task<bool> AddClientAsync(ClientInfo client, CancellationToken ct)
    {
        ThrowIfUnavailable();
        lock (_lock)
            return Task.FromResult(_clients.TryAdd(client.Id, client));
    }

    public Task<ClientInfo?> GetClientAsync(string clientId, CancellationToken ct)
    {
        Interlocked.Increment(ref _clientReads);
        ThrowIfUnavailable();
        lock (_lock)
            return Task.FromResult(_clients.GetValueOrDefault(clientId));
    }

    public Task<IReadOnlyList<ClientInfo>> ListClientsAsync(CancellationToken ct)
    {
        ThrowIfUnavailable();
        lock (_lock)
            return Task.FromResult<IReadOnlyList<ClientInfo>>(_clients.Values.OrderBy(c => c.Id, StringComparer.Ordinal).ToList());
    }

    public Task<ClientInfo?> UpdateClientAsync(string clientId, string? name, bool? disabled, DateTimeOffset now, CancellationToken ct)
    {
        ThrowIfUnavailable();
        lock (_lock)
        {
            if (!_clients.TryGetValue(clientId, out var client))
                return Task.FromResult<ClientInfo?>(null);

            var disabledAt = disabled switch
            {
                true => client.DisabledAt ?? now,
                false => null,
                null => client.DisabledAt
            };

            var updated = client with { Name = name ?? client.Name, DisabledAt = disabledAt };
            _clients[clientId] = updated;
            return Task.FromResult<ClientInfo?>(updated);
        }
    }

    public Task<bool> AddKeyAsync(ApiKeyInfo key, CancellationToken ct)
    {
        ThrowIfUnavailable();
        lock (_lock)
            return Task.FromResult(_keys.TryAdd(key.KeyId, key));
    }

    public Task<IReadOnlyList<ApiKeyInfo>> ListKeysAsync(string clientId, CancellationToken ct)
    {
        ThrowIfUnavailable();
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<ApiKeyInfo>>(_keys.Values
                .Where(k => k.ClientId == clientId)
                .OrderByDescending(k => k.CreatedAt)
                .ThenBy(k => k.KeyId, StringComparer.Ordinal)
                .ToList());
        }
    }

    public Task<ApiKeyInfo?> RevokeKeyAsync(string clientId, string keyId, DateTimeOffset now, CancellationToken ct)
    {
        ThrowIfUnavailable();
        lock (_lock)
        {
            if (!_keys.TryGetValue(keyId, out var key) || key.ClientId != clientId)
                return Task.FromResult<ApiKeyInfo?>(null);

            var revoked = key with { RevokedAt = key.RevokedAt ?? now };
            _keys[keyId] = revoked;
            return Task.FromResult<ApiKeyInfo?>(revoked);
        }
    }

    public Task RecordLastUsedAsync(IReadOnlyCollection<string> keyIds, DateTimeOffset usedAt, CancellationToken ct)
    {
        ThrowIfUnavailable();
        lock (_lock)
        {
            foreach (var keyId in keyIds)
            {
                if (_keys.TryGetValue(keyId, out var key) && (key.LastUsedAt is null || key.LastUsedAt < usedAt))
                    _keys[keyId] = key with { LastUsedAt = usedAt };
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Writes a key straight into the store, as if issued earlier, and returns the full key.</summary>
    public string Seed(
        string clientId,
        IReadOnlyList<string> scopes,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? revokedAt = null,
        bool clientDisabled = false)
    {
        var keyId = Authentication.ApiKeyFormat.NewKeyId();
        var secret = Authentication.ApiKeyFormat.NewSecret();

        lock (_lock)
        {
            if (!_clients.ContainsKey(clientId))
                _clients[clientId] = new ClientInfo(clientId, clientId, createdAt, clientDisabled ? createdAt : null);

            _keys[keyId] = new ApiKeyInfo(
                keyId, clientId, "seeded", scopes, Authentication.KeyHasher.HashSecret(secret),
                createdAt, expiresAt, revokedAt, LastUsedAt: null);
        }

        return Authentication.ApiKeyFormat.ClientKey(StudHost.BlockCode, keyId, secret);
    }

    private void ThrowIfUnavailable()
    {
        if (Unavailable)
            throw new StorageUnavailableException(new System.Net.Sockets.SocketException());
    }
}
