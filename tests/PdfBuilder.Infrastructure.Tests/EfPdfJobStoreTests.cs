using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using PdfBuilder.Domain.Jobs;
using PdfBuilder.Infrastructure.Jobs;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Infrastructure.Tests;

/// <summary>
/// The job store against a real PostgreSQL. Every read below uses a new <c>DbContext</c> from the
/// same database, which is what a restart, or a second instance, looks like to the store.
/// </summary>
public sealed class EfPdfJobStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EfPdfJobStore StoreOn(TestDatabase database, TestClock clock, int retentionHours = 24) =>
        new(database.CreateContext(), clock, Options.Create(new JobStoreOptions { RetentionHours = retentionHours }));

    [Fact]
    public async Task Returns_null_for_an_unknown_job()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();

        Assert.Null(await StoreOn(database, new TestClock(Now)).GetAsync(Guid.NewGuid(), Ct));
    }

    [Fact]
    public async Task A_pending_job_survives_a_restart_unchanged()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        var pending = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now);

        await StoreOn(database, new TestClock(Now)).SaveAsync(pending, Ct);
        var afterRestart = await StoreOn(database, new TestClock(Now)).GetAsync(pending.JobId, Ct);

        Assert.Equal(pending, afterRestart);
    }

    [Fact]
    public async Task A_succeeded_job_survives_a_restart_unchanged()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        var succeeded = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now)
            .Succeed("a7e0495b2cd24ed9bb65b1b0f3a14c20", "http://x/files/a7e0495b2cd24ed9bb65b1b0f3a14c20", 64_210, Now.AddSeconds(4));

        await StoreOn(database, new TestClock(Now)).SaveAsync(succeeded, Ct);
        var afterRestart = await StoreOn(database, new TestClock(Now)).GetAsync(succeeded.JobId, Ct);

        Assert.Equal(succeeded, afterRestart);
    }

    [Fact]
    public async Task A_failed_job_survives_a_restart_unchanged()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        var failed = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now)
            .Fail("TEMPLATE_RENDER_FAILED", "line 3: unexpected token", Now.AddSeconds(1));

        await StoreOn(database, new TestClock(Now)).SaveAsync(failed, Ct);
        var afterRestart = await StoreOn(database, new TestClock(Now)).GetAsync(failed.JobId, Ct);

        Assert.Equal(failed, afterRestart);
    }

    [Fact]
    public async Task Saving_again_replaces_the_job_it_belongs_to()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        var pending = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now);
        var clock = new TestClock(Now);

        await StoreOn(database, clock).SaveAsync(pending, Ct);
        await StoreOn(database, clock).SaveAsync(pending.Succeed("f1", "http://x/files/f1", 2048, Now.AddSeconds(3)), Ct);

        var job = await StoreOn(database, clock).GetAsync(pending.JobId, Ct);
        Assert.Equal(PdfJobStatus.Succeeded, job!.Status);
        Assert.Equal("f1", job.FileId);
        Assert.Equal(2048, job.SizeBytes);
        Assert.Null(job.ErrorCode);
    }

    [Fact]
    public async Task Saving_a_job_the_store_has_never_seen_creates_it()
    {
        // A producer that publishes straight to RabbitMQ has no pending row; the consumer's first
        // write is the completed state.
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        var completed = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now).Succeed("f1", "http://x/files/f1", 1, Now);

        await StoreOn(database, new TestClock(Now)).SaveAsync(completed, Ct);

        Assert.Equal(completed, await StoreOn(database, new TestClock(Now)).GetAsync(completed.JobId, Ct));
    }

    [Fact]
    public async Task Two_instances_saving_the_same_job_in_turn_leave_the_later_state()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        var pending = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now);
        var clock = new TestClock(Now);

        await StoreOn(database, clock).SaveAsync(pending, Ct);
        await StoreOn(database, clock).SaveAsync(pending.Fail("RENDER_FAILED", "crashed", Now.AddSeconds(2)), Ct);

        var job = await StoreOn(database, clock).GetAsync(pending.JobId, Ct);
        Assert.Equal(PdfJobStatus.Failed, job!.Status);
    }

    [Fact]
    public async Task A_job_another_instance_records_between_the_look_and_the_write_is_updated_not_lost()
    {
        // The window is tiny in real life, so the test makes it: a competing instance writes the same
        // new job after this store has looked and found nothing, but before its insert runs.
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        var clock = new TestClock(Now);
        var pending = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now);
        var failed = pending.Fail("RENDER_FAILED", "crashed", Now.AddSeconds(2));

        var competitor = new CompetingWriter(() => StoreOn(database, clock).SaveAsync(pending, Ct));
        var options = new DbContextOptionsBuilder<PdfBuilderDbContext>();
        options.Configure(database.ConnectionString).AddInterceptors(competitor);
        var store = new EfPdfJobStore(
            new PdfBuilderDbContext(options.Options), clock, Options.Create(new JobStoreOptions()));

        await store.SaveAsync(failed, Ct);

        Assert.True(competitor.Fired);
        Assert.Equal(failed, await StoreOn(database, clock).GetAsync(pending.JobId, Ct));
    }

    /// <summary>Runs a write on another connection just before the first <c>SaveChanges</c> of the context it is attached to.</summary>
    private sealed class CompetingWriter(Func<Task> write) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await write();
            }

            return result;
        }
    }

    [Fact]
    public async Task A_job_is_available_for_exactly_the_retention_period_after_its_last_save()
    {
        // Parity with the in-memory store: every save gave the job a fresh expiry.
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        var job = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now);
        var clock = new TestClock(Now);

        await StoreOn(database, clock, retentionHours: 1).SaveAsync(job, Ct);

        clock.Now = Now.AddMinutes(59);
        Assert.NotNull(await StoreOn(database, clock, retentionHours: 1).GetAsync(job.JobId, Ct));

        clock.Now = Now.AddMinutes(61);
        Assert.Null(await StoreOn(database, clock, retentionHours: 1).GetAsync(job.JobId, Ct));
    }

    [Fact]
    public async Task Saving_again_gives_the_job_a_fresh_retention_period()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        var pending = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now);
        var clock = new TestClock(Now);
        await StoreOn(database, clock, retentionHours: 1).SaveAsync(pending, Ct);

        clock.Now = Now.AddMinutes(50);
        await StoreOn(database, clock, retentionHours: 1)
            .SaveAsync(pending.Succeed("f1", "http://x/files/f1", 1, clock.Now), Ct);

        // 110 minutes after the first save, but only 60 after the last one.
        clock.Now = Now.AddMinutes(110);
        Assert.NotNull(await StoreOn(database, clock, retentionHours: 1).GetAsync(pending.JobId, Ct));
    }

    [Fact]
    public async Task A_job_can_be_read_from_a_second_instance()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        var job = PdfJob.Enqueued(Guid.NewGuid(), "invoice", Now);
        var clock = new TestClock(Now);

        var instanceA = StoreOn(database, clock);
        var instanceB = StoreOn(database, clock);
        await instanceA.SaveAsync(job, Ct);

        Assert.Equal(job, await instanceB.GetAsync(job.JobId, Ct));
    }
}
