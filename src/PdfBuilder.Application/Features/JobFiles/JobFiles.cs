using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Exceptions;
using PdfBuilder.Domain.Jobs;

namespace PdfBuilder.Application.Features.JobFiles;

/// <summary>A job's PDF, opened for reading. The caller disposes <see cref="Content"/>.</summary>
public sealed record JobFile(string FileId, Stream Content);

/// <summary>
/// What a caller may do with the file of a job they own: see whether it is still there, ask for a link,
/// download it. A job another client owns is never found, so it answers as an unknown one.
/// </summary>
/// <remarks>
/// This service makes no URLs: links are built where the request is served, from the file id.
/// A file found missing from storage is recorded on the job, so the next read says so without asking again.
/// </remarks>
public sealed class JobFiles(IPdfJobStore jobs, IFileStorage storage)
{
    /// <summary>The job, with <see cref="PdfJob.FileExpired"/> true when its file is gone. Null for an unknown job.</summary>
    public async Task<PdfJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await jobs.GetAsync(jobId, cancellationToken);
        if (job is not { Status: PdfJobStatus.Succeeded, FileExpired: false, FileId: { } fileId })
            return job;

        if (await storage.ExistsAsync(fileId, cancellationToken))
            return job;

        await jobs.MarkFileMissingAsync(jobId, cancellationToken);
        return job.WithFileExpired();
    }

    /// <summary>The file id of the job's file, to make a link from. Null for an unknown job.</summary>
    /// <exception cref="JobHasNoFileException">The job is pending or failed.</exception>
    /// <exception cref="FileExpiredException">The file was deleted or is missing.</exception>
    public async Task<string?> RequireFileAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await GetJobAsync(jobId, cancellationToken);
        return job is null ? null : FileIdOf(job);
    }

    /// <summary>Opens the job's file. Null for an unknown job.</summary>
    /// <exception cref="JobHasNoFileException">The job is pending or failed.</exception>
    /// <exception cref="FileExpiredException">The file was deleted or is missing.</exception>
    public async Task<JobFile?> OpenFileAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await jobs.GetAsync(jobId, cancellationToken);
        if (job is null)
            return null;

        var fileId = FileIdOf(job);
        var stream = await storage.OpenReadAsync(fileId, cancellationToken);
        if (stream is null)
        {
            await jobs.MarkFileMissingAsync(jobId, cancellationToken);
            throw new FileExpiredException();
        }

        return new JobFile(fileId, stream);
    }

    private static string FileIdOf(PdfJob job)
    {
        if (job.Status != PdfJobStatus.Succeeded || job.FileId is null)
            throw new JobHasNoFileException();

        return job.FileExpired ? throw new FileExpiredException() : job.FileId;
    }
}
