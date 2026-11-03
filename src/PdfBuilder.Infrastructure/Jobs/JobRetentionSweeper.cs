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

        var removed = await db.Set<PdfJobRecord>()
            .Where(j => j.LastSavedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        PersistenceLog.JobsSwept(logger, removed, hours);
        return removed;
    }

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
