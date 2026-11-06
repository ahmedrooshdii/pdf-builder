using NSubstitute;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Application.Features.JobFiles;
using PdfBuilder.Domain.Exceptions;
using PdfBuilder.Domain.Jobs;

namespace PdfBuilder.Application.Tests;

/// <summary>What a caller can do with the file of a job: the rules of research R5, with stores that are stand-ins.</summary>
public sealed class JobFilesTests
{
    private const string FileId = "a7e0495b2cd24ed9bb65b1b0f3a14c20";
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IPdfJobStore _jobs = Substitute.For<IPdfJobStore>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();

    private JobFiles Files => new(_jobs, _storage);

    private static PdfJob Pending(Guid id) => PdfJob.Enqueued(id, "invoice", Now);

    private static PdfJob Succeeded(Guid id) => Pending(id).Succeed(FileId, 10, Now, Now);

    private static PdfJob Failed(Guid id) => Pending(id).Fail("RENDER_FAILED", "boom", Now);

    private PdfJob Has(PdfJob job, bool fileExists = true)
    {
        _jobs.GetAsync(job.JobId, Arg.Any<CancellationToken>()).Returns(job);
        _storage.ExistsAsync(FileId, Arg.Any<CancellationToken>()).Returns(fileExists);
        return job;
    }

    [Fact]
    public async Task An_unknown_job_has_no_file_id_and_is_not_an_error()
    {
        Assert.Null(await Files.RequireFileAsync(Guid.NewGuid(), Ct));
        Assert.Null(await Files.OpenFileAsync(Guid.NewGuid(), Ct));
        Assert.Null(await Files.GetJobAsync(Guid.NewGuid(), Ct));
    }

    [Fact]
    public async Task A_pending_or_failed_job_has_no_file()
    {
        var pending = Has(Pending(Guid.NewGuid()));
        var failed = Has(Failed(Guid.NewGuid()));

        await Assert.ThrowsAsync<JobHasNoFileException>(() => Files.RequireFileAsync(pending.JobId, Ct));
        await Assert.ThrowsAsync<JobHasNoFileException>(() => Files.RequireFileAsync(failed.JobId, Ct));
        await Assert.ThrowsAsync<JobHasNoFileException>(() => Files.OpenFileAsync(failed.JobId, Ct));
    }

    [Fact]
    public async Task Storage_is_not_asked_about_a_job_that_has_no_file()
    {
        var pending = Has(Pending(Guid.NewGuid()));
        var failed = Has(Failed(Guid.NewGuid()));

        await Files.GetJobAsync(pending.JobId, Ct);
        await Files.GetJobAsync(failed.JobId, Ct);

        await _storage.DidNotReceiveWithAnyArgs().ExistsAsync(default!, Ct);
    }

    [Fact]
    public async Task A_job_whose_file_expired_says_so()
    {
        var job = Has(Succeeded(Guid.NewGuid()).WithFileExpired());

        await Assert.ThrowsAsync<FileExpiredException>(() => Files.RequireFileAsync(job.JobId, Ct));
        await Assert.ThrowsAsync<FileExpiredException>(() => Files.OpenFileAsync(job.JobId, Ct));
        await _storage.DidNotReceiveWithAnyArgs().ExistsAsync(default!, Ct);
    }

    [Fact]
    public async Task A_job_whose_file_is_there_gives_the_file_id()
    {
        var job = Has(Succeeded(Guid.NewGuid()));

        Assert.Equal(FileId, await Files.RequireFileAsync(job.JobId, Ct));
    }

    [Fact]
    public async Task A_file_found_missing_from_storage_is_recorded_once_and_the_job_reports_it_expired()
    {
        var job = Has(Succeeded(Guid.NewGuid()), fileExists: false);

        var read = await Files.GetJobAsync(job.JobId, Ct);

        Assert.True(read!.FileExpired);
        await _jobs.Received(1).MarkFileMissingAsync(job.JobId, Arg.Any<CancellationToken>());
        await Assert.ThrowsAsync<FileExpiredException>(() => Files.RequireFileAsync(job.JobId, Ct));
    }

    [Fact]
    public async Task Opening_a_file_that_vanished_since_the_job_was_read_marks_it_and_says_it_expired()
    {
        var job = Has(Succeeded(Guid.NewGuid()));
        _storage.OpenReadAsync(FileId, Arg.Any<CancellationToken>()).Returns((Stream?)null);

        await Assert.ThrowsAsync<FileExpiredException>(() => Files.OpenFileAsync(job.JobId, Ct));

        await _jobs.Received(1).MarkFileMissingAsync(job.JobId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Opening_a_file_that_is_there_returns_its_stream_and_id()
    {
        var job = Has(Succeeded(Guid.NewGuid()));
        using var content = new MemoryStream([1, 2, 3]);
        _storage.OpenReadAsync(FileId, Arg.Any<CancellationToken>()).Returns(content);

        var file = await Files.OpenFileAsync(job.JobId, Ct);

        Assert.Equal(FileId, file!.FileId);
        Assert.Same(content, file.Content);
        await _jobs.DidNotReceiveWithAnyArgs().MarkFileMissingAsync(default, Ct);
    }
}
