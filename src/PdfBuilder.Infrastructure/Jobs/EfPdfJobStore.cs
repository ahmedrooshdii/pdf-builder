using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Exceptions;
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
/// <para>
/// A job belongs to the client that submitted it. The context's <c>"client"</c> query filter confines
/// every read here to the caller's own jobs, so another client's job id answers as if it never existed.
/// </para>
/// </remarks>
public sealed class EfPdfJobStore(
    PdfBuilderDbContext db,
    ClientScope scope,
    TimeProvider clock,
    IOptions<JobStoreOptions> options) : IPdfJobStore
{
    public async Task SaveAsync(PdfJob job, CancellationToken cancellationToken = default)
    {
        var record = PdfJobRecord.From(job, scope.RequireClientId(), clock.GetUtcNow());
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

            // The id exists, but this caller cannot see it: it is another client's job. Say so, rather than
            // rethrowing a database error, which the consumer would take for a transient fault and retry.
            if (winner is null)
                throw new JobIdTakenException();

            winner.CopyFrom(record);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<PdfJob> ClaimAsync(Guid jobId, string templateKey, CancellationToken cancellationToken = default)
    {
        var jobs = db.Set<PdfJobRecord>();

        // The caller's own job with this id, however old: the retention cut-off belongs to reads for
        // callers, and a job that is being worked on again must not be mistaken for someone else's.
        var own = await jobs.AsNoTracking().FirstOrDefaultAsync(j => j.JobId == jobId, cancellationToken);
        if (own is not null)
            return own.ToDomain();

        var now = clock.GetUtcNow();
        var record = PdfJobRecord.From(PdfJob.Enqueued(jobId, templateKey, now), scope.RequireClientId(), now);
        jobs.Add(record);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return record.ToDomain();
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            db.Entry(record).State = EntityState.Detached;

            // The id is taken. By this caller, through another instance that got there first: fine, it is its job.
            // By anyone else: the caller-filtered read cannot see it, and nothing about it is read or reported.
            var winner = await jobs.AsNoTracking().FirstOrDefaultAsync(j => j.JobId == jobId, cancellationToken);
            return winner is null ? throw new JobIdTakenException() : winner.ToDomain();
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
