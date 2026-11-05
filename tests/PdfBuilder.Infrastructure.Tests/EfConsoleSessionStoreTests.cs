using System.Security.Cryptography;
using Blocks.ServiceDefaults.EntityFramework;
using Blocks.ServiceDefaults.Identity;
using Blocks.ServiceDefaults.Sessions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Infrastructure.Tests;

/// <summary>The shared console session store and Data Protection key ring over PDF Builder's own database.</summary>
public sealed class EfConsoleSessionStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICurrentClient>(_ => new FixedCurrentClient());   // the context needs a caller; these tests are about no client in particular
        services.AddDbContext<PdfBuilderDbContext>(options => options.Configure(connectionString));
        services.AddBlocksConsoleSessionStore<PdfBuilderDbContext>();
        services.AddOptions<Blocks.ServiceDefaults.BlocksAuthOptions>().Configure(o => o.BlockCode = "pdf");
        services.AddBlocksDataProtection<PdfBuilderDbContext>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task WithStoreAsync(ServiceProvider provider, Func<IConsoleSessionStore, Task> action)
    {
        await using var scope = provider.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<IConsoleSessionStore>());
    }

    private static async Task<ConsoleSession?> FindAsync(ServiceProvider provider, byte[] idHash)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IConsoleSessionStore>().FindAsync(idHash, Ct);
    }

    private static ConsoleSession NewSession(DateTimeOffset createdAt, byte[]? idHash = null, byte[]? fingerprint = null) => new(
        idHash ?? RandomNumberGenerator.GetBytes(32),
        createdAt,
        createdAt.AddHours(12),
        RevokedAt: null,
        fingerprint ?? RandomNumberGenerator.GetBytes(16));

    [Fact]
    public async Task A_session_is_read_back_by_its_id_hash()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(database.ConnectionString);
        var session = NewSession(Now);

        await WithStoreAsync(provider, s => s.CreateAsync(session, Ct));
        var found = await FindAsync(provider, session.IdHash);

        Assert.NotNull(found);
        Assert.Equal(session.ExpiresAt, found.ExpiresAt);
        Assert.Equal(session.AdminKeyFingerprint, found.AdminKeyFingerprint);
        Assert.Null(found.RevokedAt);
        Assert.Null(await FindAsync(provider, RandomNumberGenerator.GetBytes(32)));
    }

    [Fact]
    public async Task Revoking_sets_the_time_once()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(database.ConnectionString);
        var session = NewSession(Now);
        await WithStoreAsync(provider, s => s.CreateAsync(session, Ct));

        await WithStoreAsync(provider, s => s.RevokeAsync(session.IdHash, Now.AddHours(1), Ct));
        await WithStoreAsync(provider, s => s.RevokeAsync(session.IdHash, Now.AddHours(2), Ct));

        Assert.Equal(Now.AddHours(1), (await FindAsync(provider, session.IdHash))!.RevokedAt);
    }

    [Fact]
    public async Task Only_sessions_that_expired_before_the_cutoff_are_deleted()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(database.ConnectionString);
        var old = NewSession(Now.AddDays(-3));
        var recent = NewSession(Now.AddHours(-13));
        await WithStoreAsync(provider, s => s.CreateAsync(old, Ct));
        await WithStoreAsync(provider, s => s.CreateAsync(recent, Ct));

        await WithStoreAsync(provider, s => s.DeleteExpiredBeforeAsync(Now.AddDays(-1), Ct));

        Assert.Null(await FindAsync(provider, old.IdHash));
        Assert.NotNull(await FindAsync(provider, recent.IdHash));
    }

    [Fact]
    public async Task The_database_refuses_a_malformed_id_hash_or_fingerprint()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(database.ConnectionString);

        var shortHash = await Assert.ThrowsAnyAsync<Exception>(() =>
            WithStoreAsync(provider, s => s.CreateAsync(NewSession(Now, idHash: new byte[31]), Ct)));
        Assert.Contains("ck_console_sessions_id_hash_length", Constraint(shortHash), StringComparison.Ordinal);

        var shortFingerprint = await Assert.ThrowsAnyAsync<Exception>(() =>
            WithStoreAsync(provider, s => s.CreateAsync(NewSession(Now, fingerprint: new byte[15]), Ct)));
        Assert.Contains("ck_console_sessions_fingerprint_length", Constraint(shortFingerprint), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_key_ring_is_shared_through_the_database()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var first = BuildProvider(database.ConnectionString);
        await using var second = BuildProvider(database.ConnectionString);

        var protectedValue = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("hello");
        var unprotected = second.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(protectedValue);

        Assert.Equal("hello", unprotected);
    }

    private static string Constraint(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
                return postgresException.ConstraintName ?? string.Empty;
        }

        return string.Empty;
    }
}
