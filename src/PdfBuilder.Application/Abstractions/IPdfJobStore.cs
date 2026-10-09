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

    /// <returns>The job, or <c>null</c> when it is unknown or its retention window has passed.</returns>
    Task<PdfJob?> GetAsync(Guid jobId, CancellationToken cancellationToken = default);
}
