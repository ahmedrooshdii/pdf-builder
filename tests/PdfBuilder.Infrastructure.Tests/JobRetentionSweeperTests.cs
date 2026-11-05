using Blocks.ServiceDefaults.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PdfBuilder.Domain.Jobs;
using PdfBuilder.Infrastructure.Jobs;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.Infrastructure.Persistence.Records;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Infrastructure.Tests;

public sealed class JobRetentionSweeperTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A job store for one client, on a context of its own.</summary>
    private static EfPdfJobStore StoreFor(TestDatabase database, string clientId, TestClock clock, IOptions<JobStoreOptions> options)
    {
        var caller = new FixedCurrentClient(clientId);
        return new EfPdfJobStore(database.CreateContext(caller), new ClientScope(caller), clock, options);
    }

    /// <summary>The sweeper runs with no caller, as it does in the service: its scope has a context for nobody.</summary>
    private static ServiceProvider SweeperProvider(TestDatabase database) => new ServiceCollection()
        .AddScoped<ICurrentClient>(_ => new FixedCurrentClient())
        .AddDbContext<PdfBuilderDbContext>(o => o.Configure(database.ConnectionString))
        .BuildServiceProvider();

    [Fact]
    public async Task One_sweep_removes_the_jobs_past_retention_and_keeps_the_rest()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        var clock = new TestClock(Now);
        var options = Options.Create(new JobStoreOptions { RetentionHours = 24 });

        var old = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now);
        var recent = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now);
        await StoreFor(database, "alpha", clock, options).SaveAsync(old, Ct);

        clock.Now = Now.AddHours(20);
        await StoreFor(database, "alpha", clock, options).SaveAsync(recent, Ct);

        // 25 hours after the first save, 5 after the second.
        clock.Now = Now.AddHours(25);
        await using var provider = SweeperProvider(database);
        var sweeper = new JobRetentionSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(), options, clock, NullLogger<JobRetentionSweeper>.Instance);

        var removed = await sweeper.SweepAsync(Ct);

        Assert.Equal(1, removed);
        await using var check = database.CreateContext("alpha");
        Assert.Equal([recent.JobId], await check.Set<PdfJobRecord>().Select(j => j.JobId).ToListAsync(Ct));
    }

    [Fact]
    public async Task A_sweep_of_an_empty_table_removes_nothing()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        await using var provider = SweeperProvider(database);
        var sweeper = new JobRetentionSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new JobStoreOptions()),
            new TestClock(Now),
            NullLogger<JobRetentionSweeper>.Instance);

        Assert.Equal(0, await sweeper.SweepAsync(Ct));
    }

    [Fact]
    public async Task One_sweep_removes_the_expired_jobs_of_every_client_and_keeps_the_rest()
    {
        // The sweeper has no caller, and the client filter matches nothing without one: it has to say that it
        // is looking at every client's jobs, or it would quietly delete nothing and the table would grow.
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        var clock = new TestClock(Now);
        var options = Options.Create(new JobStoreOptions { RetentionHours = 24 });

        var oldAlpha = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now);
        var oldBeta = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now);
        await StoreFor(database, "alpha", clock, options).SaveAsync(oldAlpha, Ct);
        await StoreFor(database, "beta", clock, options).SaveAsync(oldBeta, Ct);

        clock.Now = Now.AddHours(20);
        var recentAlpha = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now);
        var recentBeta = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now);
        await StoreFor(database, "alpha", clock, options).SaveAsync(recentAlpha, Ct);
        await StoreFor(database, "beta", clock, options).SaveAsync(recentBeta, Ct);

        clock.Now = Now.AddHours(25);
        await using var provider = SweeperProvider(database);
        var sweeper = new JobRetentionSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(), options, clock, NullLogger<JobRetentionSweeper>.Instance);

        var removed = await sweeper.SweepAsync(Ct);

        Assert.Equal(2, removed);
        await using var alpha = database.CreateContext("alpha");
        await using var beta = database.CreateContext("beta");
        Assert.Equal([recentAlpha.JobId], await alpha.Set<PdfJobRecord>().Select(j => j.JobId).ToListAsync(Ct));
        Assert.Equal([recentBeta.JobId], await beta.Set<PdfJobRecord>().Select(j => j.JobId).ToListAsync(Ct));
    }
}
