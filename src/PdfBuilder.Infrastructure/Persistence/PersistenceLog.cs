using Microsoft.Extensions.Logging;

namespace PdfBuilder.Infrastructure.Persistence;

/// <summary>Source-generated log methods for the database: migrations, the template import, retention.</summary>
internal static partial class PersistenceLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Database schema brought up to date; migrations that were pending when this instance started: {Migrations}")]
    public static partial void MigrationsApplied(ILogger logger, string migrations);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Database schema is up to date")]
    public static partial void SchemaUpToDate(ILogger logger);

    [LoggerMessage(
        EventId = 3010,
        Level = LogLevel.Information,
        Message = "Imported {ImportedCount} authored template(s) from {SourcePath}; skipped {SkippedCount}")]
    public static partial void TemplateImportCompleted(ILogger logger, int importedCount, string sourcePath, int skippedCount);

    [LoggerMessage(
        EventId = 3011,
        Level = LogLevel.Warning,
        Message = "Skipped template file {File} ({Reason}): {Detail}")]
    public static partial void TemplateFileSkipped(ILogger logger, string file, string reason, string? detail);

    [LoggerMessage(
        EventId = 3012,
        Level = LogLevel.Debug,
        Message = "Authored templates were already imported on {ImportedAt}; not importing again")]
    public static partial void TemplateImportAlreadyDone(ILogger logger, DateTimeOffset importedAt);

    [LoggerMessage(
        EventId = 3020,
        Level = LogLevel.Debug,
        Message = "Retention sweep removed {Count} job(s) older than {RetentionHours} hour(s)")]
    public static partial void JobsSwept(ILogger logger, int count, int retentionHours);

    [LoggerMessage(
        EventId = 3021,
        Level = LogLevel.Warning,
        Message = "Retention sweep failed; it will try again next hour")]
    public static partial void JobSweepFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 3030,
        Level = LogLevel.Information,
        Message = "Client {ClientId} was not registered; registered it as the owner of data from before clients existed. Rename it in the console")]
    public static partial void DefaultClientRegistered(ILogger logger, string clientId);

    [LoggerMessage(
        EventId = 3031,
        Level = LogLevel.Information,
        Message = "Gave {TemplateCount} authored template(s) and {JobCount} job(s) from before clients existed to client {ClientId}")]
    public static partial void ClientOwnerAssigned(ILogger logger, int templateCount, int jobCount, string clientId);

    [LoggerMessage(
        EventId = 3040,
        Level = LogLevel.Debug,
        Message = "File sweep marked {Marked} job(s) and deleted {Deleted} file(s) older than {RetentionDays} day(s)")]
    public static partial void FilesSwept(ILogger logger, int marked, int deleted, int retentionDays);

    [LoggerMessage(
        EventId = 3041,
        Level = LogLevel.Warning,
        Message = "File sweep failed; it will try again in 15 minutes")]
    public static partial void FileSweepFailed(ILogger logger, Exception exception);
}
