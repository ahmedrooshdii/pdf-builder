using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Templates;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.Infrastructure.Persistence.Records;

namespace PdfBuilder.Infrastructure.Templates;

/// <summary>
/// Carries authored templates from the files the previous version kept into the database, once.
/// </summary>
/// <remarks>
/// <para>
/// "Once" is per deployment, however many instances start together. The work happens in one
/// transaction that first takes a PostgreSQL advisory lock: an instance that arrives while another
/// is importing waits, then finds the <see cref="TemplateImportRecord"/> the first one wrote and
/// does nothing. The record is written even when there was nothing to import, so a later start
/// never scans the directory again, and a template deleted after the upgrade is not brought back
/// by files that are still lying around.
/// </para>
/// <para>The files are read, never changed or removed, so an upgrade can be rolled back.</para>
/// </remarks>
internal sealed class TemplateImporter(
    IOptions<TemplateStoreOptions> options,
    IBuiltInTemplateSource builtIn,
    ClientOwnerAssignment clientOwner,
    TimeProvider clock,
    ILogger<TemplateImporter> logger)
{
    // Any constant will do; it only has to be the same in every instance, and not used for anything else.
    private const long AdvisoryLockKey = 0x5044_4642_5455_4C31;

    public async Task ImportAsync(PdfBuilderDbContext db, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Held until this transaction ends, which is also when the record below becomes visible.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", cancellationToken);

        var done = await db.Set<TemplateImportRecord>().AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (done is not null)
        {
            PersistenceLog.TemplateImportAlreadyDone(logger, done.CompletedAt);
            return;
        }

        var source = Path.GetFullPath(options.Value.AuthoredRootPath, AppContext.BaseDirectory);
        var read = await LegacyTemplateFiles.ReadAsync(source, cancellationToken);

        // A built-in template wins over an authored one with the same key, in any letter case, as it
        // always has in the catalog; importing it would only create something that can never be used.
        var builtInKeys = (await builtIn.ListAsync(cancellationToken))
            .Select(template => template.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var skipped = new List<SkippedTemplateFile>(read.Skipped);
        var toImport = new List<AuthoredTemplate>();

        foreach (var template in read.Templates)
        {
            if (builtInKeys.Contains(template.Key))
                skipped.Add(new SkippedTemplateFile(
                    $"{template.Key}.json", SkipReasons.BuiltInKey, "A built-in template has this key."));
            else
                toImport.Add(template);
        }

        // Templates belong to a client, and only the operator knows which one these belong to. Ask only now,
        // when there is something to give an owner: a fresh install, or one with nothing to import, needs no
        // setting. If the setting is missing this throws before anything is written, and the transaction
        // (and the advisory lock) is released, so the next start finds exactly what this one did.
        var imported = new List<AuthoredTemplateRecord>();
        if (toImport.Count > 0)
        {
            var owner = await clientOwner.RequireOwnerAsync(db, cancellationToken);
            imported.AddRange(toImport.Select(template => AuthoredTemplateRecord.From(template, owner)));
        }

        db.Set<AuthoredTemplateRecord>().AddRange(imported);
        db.Set<TemplateImportRecord>().Add(new TemplateImportRecord
        {
            CompletedAt = clock.GetUtcNow().ToUniversalTime(),
            SourcePath = source,
            ImportedCount = imported.Count,
            SkippedJson = TemplateImportRecord.Serialize(skipped)
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var file in skipped)
            PersistenceLog.TemplateFileSkipped(logger, file.File, file.Reason, file.Detail);

        PersistenceLog.TemplateImportCompleted(logger, imported.Count, source, skipped.Count);
    }
}
