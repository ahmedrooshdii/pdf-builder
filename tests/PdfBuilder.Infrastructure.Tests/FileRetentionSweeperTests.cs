using Blocks.ServiceDefaults.Files;
using Blocks.ServiceDefaults.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Jobs;
using PdfBuilder.Infrastructure.Jobs;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.Infrastructure.Persistence.Records;
using PdfBuilder.Infrastructure.Storage;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Infrastructure.Tests;

/// <summary>
/// Feature 005, US3: files past retention are deleted, the jobs they belonged to say so, and a failure only
/// postpones it (research R6).
/// </summary>
public sealed class FileRetentionSweeperTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pdf-builder-file-sweeper-{Guid.NewGuid():N}");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>The sweeper runs with no caller, as it does in the service: its scope has a context for nobody.</summary>
    private static ServiceProvider SweeperProvider(TestDatabase database) => new ServiceCollection()
        .AddScoped<ICurrentClient>(_ => new FixedCurrentClient())
        .AddDbContext<PdfBuilderDbContext>(o => o.Configure(database.ConnectionString))
        .BuildServiceProvider();

    private static EfPdfJobStore StoreFor(TestDatabase database, string clientId, TestClock clock, int retentionDays = 30)
    {
        var caller = new FixedCurrentClient(clientId);
        return new EfPdfJobStore(
            database.CreateContext(caller), new ClientScope(caller), clock,
            Options.Create(new JobStoreOptions()),
            Options.Create(new BlocksFilesOptions { RetentionDays = retentionDays }));
    }

    private static FileRetentionSweeper SweeperFor(
        ServiceProvider provider, IFileStorage storage, TestClock clock, int retentionDays = 30) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(), storage,
            Options.Create(new BlocksFilesOptions { RetentionDays = retentionDays }), clock,
            NullLogger<FileRetentionSweeper>.Instance);

    private static async Task<PdfJob> SucceededAsync(
        TestDatabase database, string clientId, TestClock clock, DateTimeOffset fileCreatedAt)
    {
        var job = PdfJob.Enqueued(Guid.NewGuid(), "invoice", fileCreatedAt)
            .Succeed(Guid.NewGuid().ToString("N"), 10, fileCreatedAt, fileCreatedAt);
        var saved = clock.Now;
        clock.Now = fileCreatedAt;
        await StoreFor(database, clientId, clock).SaveAsync(job, Ct);
        clock.Now = saved;
        return job;
    }

    private static async Task<DateTimeOffset?> ExpiredAtAsync(TestDatabase database, string clientId, Guid jobId)
    {
        await using var context = database.CreateContext(clientId);
        return await context.Set<PdfJobRecord>().Where(j => j.JobId == jobId).Select(j => j.FileExpiredAt).SingleAsync(Ct);
    }

    /// <summary>A storage that does nothing but note what it was asked and what the database said at that moment.</summary>
    private sealed class SpyStorage(Func<Task<string>> snapshot) : IFileStorage
    {
        public List<(string Call, DateTimeOffset Cutoff, string Seen)> Calls { get; } = [];

        public bool Fail { get; set; }

        public async Task<int> DeleteCreatedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        {
            Calls.Add(("delete", cutoff, await snapshot()));
            return Fail ? throw new IOException("the disk is unhappy") : 3;
        }

        public Task<StoredFile> SaveAsync(byte[] content, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string fileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> ExistsAsync(string fileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task One_sweep_marks_the_old_files_jobs_of_every_client_and_leaves_younger_ones()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        var clock = new TestClock(Now);
        var oldAlpha = await SucceededAsync(database, "alpha", clock, Now.AddDays(-31));
        var oldBeta = await SucceededAsync(database, "beta", clock, Now.AddDays(-40));
        var youngAlpha = await SucceededAsync(database, "alpha", clock, Now.AddDays(-29));
        await using var provider = SweeperProvider(database);

        var (marked, _) = await SweeperFor(provider, new SpyStorage(() => Task.FromResult("")), clock).SweepAsync(Ct);

        Assert.Equal(2, marked);
        Assert.Equal(Now, await ExpiredAtAsync(database, "alpha", oldAlpha.JobId));
        Assert.Equal(Now, await ExpiredAtAsync(database, "beta", oldBeta.JobId));
        Assert.Null(await ExpiredAtAsync(database, "alpha", youngAlpha.JobId));
    }

    [Fact]
    public async Task Jobs_are_marked_before_the_files_are_deleted_and_with_the_same_cutoff()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha");
        var clock = new TestClock(Now);
        var old = await SucceededAsync(database, "alpha", clock, Now.AddDays(-31));
        await using var provider = SweeperProvider(database);
        var storage = new SpyStorage(async () => (await ExpiredAtAsync(database, "alpha", old.JobId)) is null ? "unmarked" : "marked");

        await SweeperFor(provider, storage, clock).SweepAsync(Ct);

        var call = Assert.Single(storage.Calls);
        Assert.Equal("marked", call.Seen);
        Assert.Equal(Now.AddDays(-30), call.Cutoff);
    }

    [Fact]
    public async Task A_storage_failure_stops_the_sweep_and_the_next_one_completes_it()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha");
        var clock = new TestClock(Now);
        await SucceededAsync(database, "alpha", clock, Now.AddDays(-31));
        await using var provider = SweeperProvider(database);
        var storage = new SpyStorage(() => Task.FromResult("")) { Fail = true };
        var sweeper = SweeperFor(provider, storage, clock);

        await Assert.ThrowsAsync<IOException>(() => sweeper.SweepAsync(Ct));

        storage.Fail = false;
        var (marked, deleted) = await sweeper.SweepAsync(Ct);
        Assert.Equal(0, marked);        // already marked by the first, failed, sweep
        Assert.Equal(3, deleted);
    }

    [Fact]
    public async Task Two_sweepers_running_at_once_both_finish_and_each_job_is_marked_once()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha");
        var clock = new TestClock(Now);
        var old = await SucceededAsync(database, "alpha", clock, Now.AddDays(-31));
        await using var provider = SweeperProvider(database);
        var first = SweeperFor(provider, new SpyStorage(() => Task.FromResult("")), clock);
        var second = SweeperFor(provider, new SpyStorage(() => Task.FromResult("")), clock);

        var results = await Task.WhenAll(first.SweepAsync(Ct), second.SweepAsync(Ct));

        Assert.Equal(1, results.Sum(r => r.Marked));
        Assert.Equal(Now, await ExpiredAtAsync(database, "alpha", old.JobId));
    }

    // ---- US5: what was there before the upgrade ---------------------------------------------------------

    [Fact]
    public async Task A_file_from_before_the_upgrade_and_its_backfilled_job_are_both_removed_by_the_first_sweep()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha");
        var clock = new TestClock(Now.AddDays(-40));
        var storage = new LocalFileStorage(Options.Create(new StorageOptions { RootPath = _root }), clock);

        // A file with no job at all, and a job whose file_created_at the migration filled in from completed_at.
        var orphan = await storage.SaveAsync([1], "application/pdf", Ct);
        var stored = await storage.SaveAsync([2], "application/pdf", Ct);
        var job = PdfJob.Enqueued(Guid.NewGuid(), "invoice", clock.Now).Succeed(stored.FileId, 1, stored.CreatedAt, stored.CreatedAt);
        await StoreFor(database, "alpha", clock).SaveAsync(job, Ct);
        clock.Now = Now;
        await using var provider = SweeperProvider(database);

        var (marked, deleted) = await SweeperFor(provider, storage, clock).SweepAsync(Ct);

        Assert.Equal(1, marked);
        Assert.Equal(2, deleted);
        Assert.False(await storage.ExistsAsync(orphan.FileId, Ct));
        Assert.False(await storage.ExistsAsync(stored.FileId, Ct));
        Assert.Equal(Now, await ExpiredAtAsync(database, "alpha", job.JobId));
    }
}
