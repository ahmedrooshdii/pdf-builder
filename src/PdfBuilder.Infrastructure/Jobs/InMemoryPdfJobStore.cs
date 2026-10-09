using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Jobs;

namespace PdfBuilder.Infrastructure.Jobs;

/// <summary>
/// Keeps job state in this process's memory for <see cref="JobStoreOptions.RetentionHours"/>.
/// </summary>
/// <remarks>
/// Single-instance only, and job state is lost on restart. That is an acceptable trade for a
/// progress indicator, but it is a real limit: behind a load balancer, a poll can land on an
/// instance that never saw the job and get a 404. Swap in a Redis or table-backed
/// <see cref="IPdfJobStore"/> before scaling out. Callers that use the message bus are unaffected -
/// they get their result from the response message, not from here.
/// </remarks>
public sealed class InMemoryPdfJobStore(IMemoryCache cache, IOptions<JobStoreOptions> options) : IPdfJobStore
{
    private readonly TimeSpan _retention = TimeSpan.FromHours(options.Value.RetentionHours);

    public Task SaveAsync(PdfJob job, CancellationToken cancellationToken = default)
    {
        cache.Set(KeyFor(job.JobId), job, _retention);
        return Task.CompletedTask;
    }

    public Task<PdfJob?> GetAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        Task.FromResult(cache.TryGetValue(KeyFor(jobId), out PdfJob? job) ? job : null);

    private static string KeyFor(Guid jobId) => $"pdf-job:{jobId:N}";
}
