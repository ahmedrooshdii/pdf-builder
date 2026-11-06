using Blocks.ServiceDefaults.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Jobs;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.Infrastructure.Persistence.Records;

namespace PdfBuilder.Infrastructure.Storage;

/// <summary>
/// Deletes the PDFs that are older than <see cref="BlocksFilesOptions.RetentionDays"/>, every fifteen minutes,
/// and marks the jobs they belonged to so that those jobs say their file expired.
/// </summary>
/// <remarks>
/// Marking comes first, then the delete, both with the same cutoff: a job never says "has a file" for a
/// file that is already gone from the sweep's side. Reads decide expiry from the file's age as well, so
/// between sweeps nothing is served past retention. Every instance runs it; marking and deleting are
/// idempotent, and a failure waits for the next tick.
/// </remarks>
internal sealed class FileRetentionSweeper(
    IServiceScopeFactory scopes,
    IFileStorage storage,
    IOptions<BlocksFilesOptions> options,
    TimeProvider clock,
    ILogger<FileRetentionSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

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
                PersistenceLog.FileSweepFailed(logger, exception);
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    /// <summary>One sweep. Returns how many jobs it marked and how many files it deleted.</summary>
    internal async Task<(int Marked, int Deleted)> SweepAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var cutoff = now - TimeSpan.FromDays(options.Value.RetentionDays);

        int marked;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PdfBuilderDbContext>();
            marked = await MarkExpiredFilesOfAllClientsAsync(db, cutoff, now, cancellationToken);
        }

        var deleted = await storage.DeleteCreatedBeforeAsync(cutoff, cancellationToken);

        PersistenceLog.FilesSwept(logger, marked, deleted, options.Value.RetentionDays);
        return (marked, deleted);
    }

    /// <summary>
    /// The sweeper has no caller, and the context's <c>"client"</c> filter matches nothing without one, so it
    /// must say that it is looking at every client's jobs. One of the few places allowed to ignore the filter;
    /// the architecture test lists them.
    /// </summary>
    private static Task<int> MarkExpiredFilesOfAllClientsAsync(
        PdfBuilderDbContext db, DateTimeOffset cutoff, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Set<PdfJobRecord>()
            .IgnoreQueryFilters([PdfBuilderDbContext.ClientFilter])
            .Where(j => j.Status == nameof(PdfJobStatus.Succeeded) && j.FileExpiredAt == null && j.FileCreatedAt < cutoff)
            .ExecuteUpdateAsync(set => set.SetProperty(j => j.FileExpiredAt, now), cancellationToken);

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
