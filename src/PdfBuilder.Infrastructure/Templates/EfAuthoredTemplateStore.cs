using Microsoft.EntityFrameworkCore;
using Npgsql;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Exceptions;
using PdfBuilder.Domain.Templates;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.Infrastructure.Persistence.Records;

namespace PdfBuilder.Infrastructure.Templates;

/// <summary>
/// Authored templates in the database: one row each, shared by every instance.
/// </summary>
/// <remarks>
/// <para>
/// The revision check that the file store did under a process-wide lock is done by the database:
/// an update names the revision it was based on and matches only that row, so two instances saving
/// from the same revision cannot both win. Keys are unique ignoring case (a unique index on a
/// lower-cased copy of the key) but are looked up exactly, as they were on disk.
/// </para>
/// <para>
/// Reads are untracked and writes that change an existing row are single statements, so a failed
/// save leaves nothing half-done in the shared context.
/// </para>
/// </remarks>
public sealed class EfAuthoredTemplateStore(PdfBuilderDbContext db) : IAuthoredTemplateStore
{
    public async Task<IReadOnlyList<AuthoredTemplate>> ListAsync(CancellationToken cancellationToken = default)
    {
        var records = await db.Set<AuthoredTemplateRecord>()
            .AsNoTracking()
            .OrderBy(t => t.Key)
            .ToListAsync(cancellationToken);

        return [.. records.Select(r => r.ToDomain())];
    }

    public async Task<AuthoredTemplate?> GetAsync(string templateKey, CancellationToken cancellationToken = default)
    {
        // Nothing that is not a valid key is stored, so there is nothing to ask the database.
        if (!TemplateKey.IsValid(templateKey))
            return null;

        var record = await db.Set<AuthoredTemplateRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Key == templateKey, cancellationToken);

        return record?.ToDomain();
    }

    public async Task AddAsync(AuthoredTemplate authoredTemplate, CancellationToken cancellationToken = default)
    {
        var record = AuthoredTemplateRecord.From(authoredTemplate);
        db.Set<AuthoredTemplateRecord>().Add(record);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Either the exact key or the same key in another letter case. The row never made it, so
            // stop tracking it, or the next save through this context would try it again.
            db.Entry(record).State = EntityState.Detached;
            throw new TemplateKeyTakenException(authoredTemplate.Key);
        }
    }

    public async Task UpdateAsync(AuthoredTemplate authoredTemplate, int expectedRevision, CancellationToken cancellationToken = default)
    {
        var record = AuthoredTemplateRecord.From(authoredTemplate);
        var key = authoredTemplate.Key;

        var updated = await db.Set<AuthoredTemplateRecord>()
            .Where(t => t.Key == key && t.Revision == expectedRevision)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(t => t.Draft, record.Draft)
                    .SetProperty(t => t.Published, record.Published)
                    .SetProperty(t => t.Revision, record.Revision)
                    .SetProperty(t => t.UpdatedAt, record.UpdatedAt)
                    .SetProperty(t => t.PublishedAt, record.PublishedAt),
                cancellationToken);

        if (updated > 0)
            return;

        // Nothing matched: either the template is gone, or someone saved since the caller read it.
        var currentRevision = await db.Set<AuthoredTemplateRecord>()
            .AsNoTracking()
            .Where(t => t.Key == key)
            .Select(t => (int?)t.Revision)
            .FirstOrDefaultAsync(cancellationToken);

        throw currentRevision is { } current
            ? new StaleTemplateRevisionException(key, expectedRevision, current)
            : new TemplateNotFoundException(key);
    }

    public async Task<bool> DeleteAsync(string templateKey, CancellationToken cancellationToken = default)
    {
        if (!TemplateKey.IsValid(templateKey))
            return false;

        var deleted = await db.Set<AuthoredTemplateRecord>()
            .Where(t => t.Key == templateKey)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }
}
