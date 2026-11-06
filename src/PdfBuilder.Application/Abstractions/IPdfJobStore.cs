using PdfBuilder.Domain.Jobs;

namespace PdfBuilder.Application.Abstractions;

/// <summary>
/// Tracks the state of accepted jobs so an HTTP caller, which cannot receive a bus response,
/// has something to poll. Writes are upserts keyed by job id, which keeps the contract
/// implementable over a cache, Redis or a table without changing callers.
/// </summary>
public interface IPdfJobStore
{
    Task SaveAsync(PdfJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes sure the caller has a job with this id before work starts on it: its own, if it has one, else a
    /// new <c>Pending</c> one recorded for the caller. Job ids are unique across every client, so an id that
    /// another client already used cannot be taken.
    /// </summary>
    /// <exception cref="Domain.Exceptions.JobIdTakenException">
    /// Another client owns a job with this id. Nothing about that job is read, changed or reported.
    /// </exception>
    Task<PdfJob> ClaimAsync(Guid jobId, string templateKey, CancellationToken cancellationToken = default);

    /// <returns>The job, or <c>null</c> when it is unknown or its retention window has passed.</returns>
    Task<PdfJob?> GetAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the caller's own job's file is gone from storage, so later reads report it expired.
    /// Does nothing for an unknown job, another client's job, or one already marked.
    /// </summary>
    Task MarkFileMissingAsync(Guid jobId, CancellationToken cancellationToken = default);
}
