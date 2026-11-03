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

    [Fact]
    public async Task One_sweep_removes_the_jobs_past_retention_and_keeps_the_rest()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        var clock = new TestClock(Now);
        var options = Options.Create(new JobStoreOptions { RetentionHours = 24 });

        var old = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now);
        var recent = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now);
        await new EfPdfJobStore(database.CreateContext(), clock, options).SaveAsync(old, Ct);

        clock.Now = Now.AddHours(20);
        await new EfPdfJobStore(database.CreateContext(), clock, options).SaveAsync(recent, Ct);

        // 25 hours after the first save, 5 after the second.
        clock.Now = Now.AddHours(25);
        await using var provider = new ServiceCollection()
            .AddDbContext<PdfBuilderDbContext>(o => o.Configure(database.ConnectionString))
            .BuildServiceProvider();
        var sweeper = new JobRetentionSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(), options, clock, NullLogger<JobRetentionSweeper>.Instance);

        var removed = await sweeper.SweepAsync(Ct);

        Assert.Equal(1, removed);
        await using var check = database.CreateContext();
        Assert.Equal([recent.JobId], await check.Set<PdfJobRecord>().Select(j => j.JobId).ToListAsync(Ct));
    }

    [Fact]
    public async Task A_sweep_of_an_empty_table_removes_nothing()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var provider = new ServiceCollection()
            .AddDbContext<PdfBuilderDbContext>(o => o.Configure(database.ConnectionString))
            .BuildServiceProvider();
        var sweeper = new JobRetentionSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new JobStoreOptions()),
            new TestClock(Now),
            NullLogger<JobRetentionSweeper>.Instance);

        Assert.Equal(0, await sweeper.SweepAsync(Ct));
    }
}
