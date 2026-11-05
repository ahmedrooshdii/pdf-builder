using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.Infrastructure.Persistence.Records;

namespace PdfBuilder.Infrastructure.Jobs;

/// <summary>
/// Deletes job rows that are past <see cref="JobStoreOptions.RetentionHours"/>, once an hour.
/// </summary>
/// <remarks>
/// Reads already treat such rows as gone, so this only bounds storage. Every instance runs it; a
/// delete of rows that are already gone is harmless, and a failure just waits for the next hour.
/// </remarks>
internal sealed class JobRetentionSweeper(
    IServiceScopeFactory scopes,
    IOptions<JobStoreOptions> options,
    TimeProvider clock,
    ILogger<JobRetentionSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);

        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                PersistenceLog.JobSweepFailed(logger, exception);
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    /// <summary>One sweep. Returns how many rows it removed.</summary>
    internal async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var hours = options.Value.RetentionHours;
        var cutoff = clock.GetUtcNow() - TimeSpan.FromHours(hours);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PdfBuilderDbContext>();

        var removed = await DeleteExpiredJobsOfAllClientsAsync(db, cutoff, cancellationToken);

        PersistenceLog.JobsSwept(logger, removed, hours);
        return removed;
    }

    /// <summary>
    /// The sweeper has no caller, and the context's <c>"client"</c> filter matches nothing without one, so it
    /// must say that it is looking at every client's jobs. The name is the point: this is one of the three
    /// places allowed to ignore the filter, and the architecture test lists them (research R2).
    /// </summary>
    private static Task<int> DeleteExpiredJobsOfAllClientsAsync(
        PdfBuilderDbContext db, DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        db.Set<PdfJobRecord>()
            .IgnoreQueryFilters([PdfBuilderDbContext.ClientFilter])
            .Where(j => j.LastSavedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
