using Blocks.ServiceDefaults.Clients;
using Blocks.ServiceDefaults.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Blocks.ServiceDefaults.EntityFramework;

/// <summary>
/// <see cref="IConsoleSessionStore"/> over the <c>console_sessions</c> table of a block's own context,
/// which must call <see cref="ModelBuilderExtensions.AddBlocksConsoleSessions"/>. "PostgreSQL is
/// unreachable" leaves as <see cref="StorageUnavailableException"/>.
/// </summary>
internal sealed class EfConsoleSessionStore<TContext>(TContext db) : IConsoleSessionStore
    where TContext : DbContext
{
    private DbSet<ConsoleSessionRecord> Sessions => db.Set<ConsoleSessionRecord>();

    public Task CreateAsync(ConsoleSession session, CancellationToken ct) => GuardAsync(async () =>
    {
        ArgumentNullException.ThrowIfNull(session);
        Sessions.Add(ConsoleSessionRecord.From(session));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return 0;
    });

    public Task<ConsoleSession?> FindAsync(byte[] idHash, CancellationToken ct) => GuardAsync(async () =>
    {
        var row = await Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.IdHash == idHash, ct);
        return row?.ToSession();
    });

    public Task RevokeAsync(byte[] idHash, DateTimeOffset at, CancellationToken ct) => GuardAsync(async () =>
    {
        var when = at.ToUniversalTime();
        return await Sessions
            .Where(s => s.IdHash == idHash && s.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedAt, when), ct);
    });

    public Task DeleteExpiredBeforeAsync(DateTimeOffset cutoff, CancellationToken ct) => GuardAsync(async () =>
    {
        var before = cutoff.ToUniversalTime();
        return await Sessions.Where(s => s.ExpiresAt < before).ExecuteDeleteAsync(ct);
    });

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
}

/// <summary>Registers the session store for a block's context.</summary>
public static class EfConsoleSessionStoreExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// <see cref="IConsoleSessionStore"/> over <typeparamref name="TContext"/>, scoped like the context.
        /// The context's model must call <see cref="ModelBuilderExtensions.AddBlocksConsoleSessions"/>.
        /// </summary>
        public IServiceCollection AddBlocksConsoleSessionStore<TContext>()
            where TContext : DbContext
        {
            services.TryAddScoped<IConsoleSessionStore, EfConsoleSessionStore<TContext>>();
            return services;
        }
    }
}
