using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PdfBuilder.Domain.Jobs;
using PdfBuilder.Infrastructure.Jobs;

namespace PdfBuilder.Infrastructure.Tests;

public sealed class InMemoryPdfJobStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryPdfJobStore _store = new(
        new MemoryCache(new MemoryCacheOptions()),
        Options.Create(new JobStoreOptions { RetentionHours = 1 }));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_null_for_an_unknown_job()
    {
        Assert.Null(await _store.GetAsync(Guid.NewGuid(), Ct));
    }

    [Fact]
    public async Task Reads_back_a_pending_job()
    {
        var jobId = Guid.NewGuid();
        await _store.SaveAsync(PdfJob.Enqueued(jobId, "invoice", Now), Ct);

        var job = await _store.GetAsync(jobId, Ct);

        Assert.NotNull(job);
        Assert.Equal(PdfJobStatus.Pending, job.Status);
        Assert.Equal("invoice", job.TemplateKey);
        Assert.Null(job.CompletedAt);
    }

    [Fact]
    public async Task Replaces_a_pending_job_with_its_success()
    {
        var jobId = Guid.NewGuid();
        var pending = PdfJob.Enqueued(jobId, "invoice", Now);
        await _store.SaveAsync(pending, Ct);

        await _store.SaveAsync(pending.Succeed("f1", "http://x/files/f1", 2048, Now.AddSeconds(3)), Ct);

        var job = await _store.GetAsync(jobId, Ct);

        Assert.Equal(PdfJobStatus.Succeeded, job!.Status);
        Assert.Equal("f1", job.FileId);
        Assert.Equal(2048, job.SizeBytes);
        Assert.Equal(Now.AddSeconds(3), job.CompletedAt);
        Assert.Null(job.ErrorCode);
    }

    [Fact]
    public async Task Replaces_a_pending_job_with_its_failure()
    {
        var jobId = Guid.NewGuid();
        var pending = PdfJob.Enqueued(jobId, "invoice", Now);
        await _store.SaveAsync(pending, Ct);

        await _store.SaveAsync(pending.Fail("TEMPLATE_NOT_FOUND", "no such template", Now.AddSeconds(1)), Ct);

        var job = await _store.GetAsync(jobId, Ct);

        Assert.Equal(PdfJobStatus.Failed, job!.Status);
        Assert.Equal("TEMPLATE_NOT_FOUND", job.ErrorCode);
        Assert.Null(job.FileUrl);
    }

    [Fact]
    public void A_completed_job_keeps_the_identity_of_the_job_it_came_from()
    {
        var jobId = Guid.NewGuid();
        var pending = PdfJob.Enqueued(jobId, "invoice", Now);

        var failed = pending.Fail("RENDER_FAILED", "crashed", Now.AddMinutes(1));

        Assert.Equal(jobId, failed.JobId);
        Assert.Equal("invoice", failed.TemplateKey);
        Assert.Equal(Now, failed.EnqueuedAt);
    }
}
