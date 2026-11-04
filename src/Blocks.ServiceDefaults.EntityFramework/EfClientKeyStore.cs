using Blocks.ServiceDefaults.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Blocks.ServiceDefaults.EntityFramework;

/// <summary>
/// <see cref="IClientKeyStore"/> over the <c>clients</c> and <c>api_keys</c> tables of a block's own
/// context, which must call <see cref="ModelBuilderExtensions.AddBlocksClients"/>.
/// </summary>
/// <remarks>
/// Every failure that means "PostgreSQL is unreachable right now" leaves as a
/// <see cref="StorageUnavailableException"/>, so callers answer 503 and never "invalid key".
/// </remarks>
internal sealed class EfClientKeyStore<TContext>(TContext db) : IClientKeyStore
    where TContext : DbContext
{
    private DbSet<ClientRecord> Clients => db.Set<ClientRecord>();
    private DbSet<ApiKeyRecord> Keys => db.Set<ApiKeyRecord>();

    public Task<KeyLookup?> FindKeyAsync(string keyId, CancellationToken ct) => GuardAsync(async () =>
    {
        var row = await (
                from key in Keys.AsNoTracking()
                join client in Clients.AsNoTracking() on key.ClientId equals client.Id
                where key.KeyId == keyId
                select new { Key = key, client.DisabledAt })
            .SingleOrDefaultAsync(ct);

        return row is null ? null : new KeyLookup(row.Key.ToInfo(), ClientDisabled: row.DisabledAt is not null);
    });

    public Task<bool> AddClientAsync(ClientInfo client, CancellationToken ct) => GuardAsync(async () =>
    {
        ArgumentNullException.ThrowIfNull(client);

        if (await Clients.AnyAsync(c => c.Id == client.Id, ct))
            return false;

        Clients.Add(new ClientRecord
        {
            Id = client.Id,
            Name = client.Name,
            CreatedAt = client.CreatedAt.ToUniversalTime(),
            DisabledAt = client.DisabledAt?.ToUniversalTime()
        });

        // Two registrations of one id can both pass the check above; the primary key settles it.
        return await TrySaveAsync(ct);
    });

    public Task<ClientInfo?> GetClientAsync(string clientId, CancellationToken ct) => GuardAsync(async () =>
    {
        var client = await Clients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == clientId, ct);
        return client is null ? null : ToInfo(client);
    });

    public Task<IReadOnlyList<ClientInfo>> ListClientsAsync(CancellationToken ct) => GuardAsync(async () =>
    {
        var clients = await Clients.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);
        return (IReadOnlyList<ClientInfo>)clients.Select(ToInfo).ToList();
    });

    public Task<ClientInfo?> UpdateClientAsync(
        string clientId, string? name, bool? disabled, DateTimeOffset now, CancellationToken ct) => GuardAsync(async () =>
    {
        var client = await Clients.SingleOrDefaultAsync(c => c.Id == clientId, ct);
        if (client is null)
            return null;

        if (name is not null)
            client.Name = name;

        // Only a change of state moves the date: disabling a disabled client keeps when it was disabled.
        if (disabled == true && client.DisabledAt is null)
            client.DisabledAt = now.ToUniversalTime();
        else if (disabled == false)
            client.DisabledAt = null;

        await db.SaveChangesAsync(ct);
        return ToInfo(client);
    });

    public Task<bool> AddKeyAsync(ApiKeyInfo key, CancellationToken ct) => GuardAsync(async () =>
    {
        ArgumentNullException.ThrowIfNull(key);

        Keys.Add(ApiKeyRecord.From(key));
        return await TrySaveAsync(ct);
    });

    public Task<IReadOnlyList<ApiKeyInfo>> ListKeysAsync(string clientId, CancellationToken ct) => GuardAsync(async () =>
    {
        var keys = await Keys.AsNoTracking()
            .Where(k => k.ClientId == clientId)
            .OrderByDescending(k => k.CreatedAt)
            .ThenBy(k => k.KeyId)
            .ToListAsync(ct);

        return (IReadOnlyList<ApiKeyInfo>)keys.Select(k => k.ToInfo()).ToList();
    });

    public Task<ApiKeyInfo?> RevokeKeyAsync(string clientId, string keyId, DateTimeOffset now, CancellationToken ct) =>
        GuardAsync(async () =>
        {
            var key = await Keys.SingleOrDefaultAsync(k => k.ClientId == clientId && k.KeyId == keyId, ct);
            if (key is null)
                return null;

            if (key.RevokedAt is null)
            {
                key.RevokedAt = now.ToUniversalTime();
                await db.SaveChangesAsync(ct);
            }

            return key.ToInfo();
        });

    public Task RecordLastUsedAsync(IReadOnlyCollection<string> keyIds, DateTimeOffset usedAt, CancellationToken ct) =>
        GuardAsync(async () =>
        {
            var ids = keyIds.ToArray();
            var at = usedAt.ToUniversalTime();

            // Never backwards: another instance may already have written a later minute.
            return await Keys
                .Where(k => ids.Contains(k.KeyId) && (k.LastUsedAt == null || k.LastUsedAt < at))
                .ExecuteUpdateAsync(set => set.SetProperty(k => k.LastUsedAt, at), ct);
        });

    /// <summary>Saves, reporting a primary-key clash as false rather than as an exception.</summary>
    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task<T> GuardAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception exception) when (NpgsqlStorageFailures.IsUnavailable(exception))
        {
            db.ChangeTracker.Clear();
            throw new StorageUnavailableException(exception);
        }
    }

    private static ClientInfo ToInfo(ClientRecord client) =>
        new(client.Id, client.Name, client.CreatedAt, client.DisabledAt);
}

/// <summary>Registers the store for a block's context.</summary>
public static class EfClientKeyStoreExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// <see cref="IClientKeyStore"/> over <typeparamref name="TContext"/>, scoped like the context.
        /// The context's model must call <see cref="ModelBuilderExtensions.AddBlocksClients"/>.
        /// </summary>
        public IServiceCollection AddBlocksClientStore<TContext>()
            where TContext : DbContext
        {
            services.TryAddScoped<IClientKeyStore, EfClientKeyStore<TContext>>();
            return services;
        }
    }
}
