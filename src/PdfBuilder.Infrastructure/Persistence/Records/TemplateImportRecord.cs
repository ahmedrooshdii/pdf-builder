using System.Text.Json;

namespace PdfBuilder.Infrastructure.Persistence.Records;

/// <summary>
/// The fact that the one-time template import has happened. Its existence is what stops every
/// later start from importing again, even if the old files are still on disk.
/// </summary>
internal sealed class TemplateImportRecord
{
    /// <summary>The only row there ever is.</summary>
    public const short SingleRowId = 1;

    public short Id { get; set; } = SingleRowId;
    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>The resolved directory the files were read from.</summary>
    public string SourcePath { get; set; } = string.Empty;

    public int ImportedCount { get; set; }

    /// <summary>The skipped files as JSON, <c>[{ "file", "reason", "detail" }]</c>; see <see cref="SkippedFiles"/>.</summary>
    public string SkippedJson { get; set; } = "[]";

    public IReadOnlyList<SkippedTemplateFile> SkippedFiles =>
        JsonSerializer.Deserialize<List<SkippedTemplateFile>>(SkippedJson, Json) ?? [];

    public static string Serialize(IReadOnlyList<SkippedTemplateFile> skipped) => JsonSerializer.Serialize(skipped, Json);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

/// <summary>A file the import left behind, and why.</summary>
/// <param name="File">The file name, without its directory.</param>
/// <param name="Reason">One of <see cref="SkipReasons"/>.</param>
/// <param name="Detail">What the reader said, for the logs and for whoever has to fix the file.</param>
internal sealed record SkippedTemplateFile(string File, string Reason, string? Detail = null);

/// <summary>The reasons a file is not imported. Stored, so treat them as a published vocabulary.</summary>
internal static class SkipReasons
{
    public const string Unreadable = "unreadable";
    public const string Malformed = "malformed";
    public const string InvalidKey = "invalid-key";
    public const string BuiltInKey = "built-in-key";
    public const string DuplicateKey = "duplicate-key";
}
