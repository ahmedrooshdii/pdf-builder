using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Jobs;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.Infrastructure.Persistence.Records;

namespace PdfBuilder.Infrastructure.Jobs;

/// <summary>
/// Keeps job state in the database, so it survives a restart and every instance sees it.
/// </summary>
/// <remarks>
/// <para>
/// A job stays readable for <see cref="JobStoreOptions.RetentionHours"/> after its <b>last save</b>,
/// which is how long the in-memory store kept it: every save gave the job a fresh expiry. Reads
/// apply that cut-off themselves, so a job disappears at the same moment it always did;
/// <see cref="JobRetentionSweeper"/> only keeps the table from growing.
/// </para>
/// <para>
/// <see cref="SaveAsync"/> goes through the context's change tracker and ends in one
/// <c>SaveChanges</c>. That is deliberate: when a message was published through the bus outbox in
/// the same scope, the outbox row and the job row are written by that one call, in one transaction.
/// </para>
/// </remarks>
public sealed class EfPdfJobStore(
    PdfBuilderDbContext db,
    TimeProvider clock,
    IOptions<JobStoreOptions> options) : IPdfJobStore
{
    public async Task SaveAsync(PdfJob job, CancellationToken cancellationToken = default)
    {
        var record = PdfJobRecord.From(job, clock.GetUtcNow());
        var jobs = db.Set<PdfJobRecord>();

        var existing = await jobs.FindAsync([job.JobId], cancellationToken);
        if (existing is null)
            jobs.Add(record);
        else
            existing.CopyFrom(record);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (existing is null && IsDuplicateKey(exception))
        {
            // Another instance recorded the same job between our read and our write, which can only
            // happen to a job that never went through the HTTP endpoint. Keep the newer state.
            db.Entry(record).State = EntityState.Detached;
            var winner = await jobs.FindAsync([job.JobId], cancellationToken);
            if (winner is null)
                throw;

            winner.CopyFrom(record);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<PdfJob?> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var cutoff = clock.GetUtcNow() - TimeSpan.FromHours(options.Value.RetentionHours);

        var record = await db.Set<PdfJobRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.JobId == jobId && j.LastSavedAt >= cutoff, cancellationToken);

        return record?.ToDomain();
    }

    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
