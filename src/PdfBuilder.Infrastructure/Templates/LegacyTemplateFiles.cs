using System.Text.Json;
using PdfBuilder.Domain.Templates;
using PdfBuilder.Infrastructure.Persistence.Records;

namespace PdfBuilder.Infrastructure.Templates;

/// <summary>
/// Reads the authored-template files the previous version kept on disk, <c>{templateKey}.json</c>
/// in <see cref="TemplateStoreOptions.AuthoredRootPath"/>, for the one-time import.
/// </summary>
/// <remarks>
/// <para>
/// The old store lived by one rule this reader keeps: the file name is the key. It also let a bad
/// file break the whole listing (a <see cref="JsonException"/> escaped). Here every file stands
/// alone: one that cannot be used is reported with a reason and the others still come through.
/// </para>
/// <para>
/// Files are only ever opened for reading, so the import can be rolled back by starting the old
/// version again.
/// </para>
/// </remarks>
internal static class LegacyTemplateFiles
{
    // What the old store wrote with: web defaults, so names are camelCase and reading is case-insensitive.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The on-disk shape the old file store wrote, kept here so it can still be read.</summary>
    private sealed record StoredTemplate(
        string? Key,
        int Revision,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? PublishedAt,
        TemplateContentRecord? Draft,
        TemplateContentRecord? Published);

    internal sealed record Result(IReadOnlyList<AuthoredTemplate> Templates, IReadOnlyList<SkippedTemplateFile> Skipped);

    public static async Task<Result> ReadAsync(string directory, CancellationToken cancellationToken)
    {
        var templates = new List<AuthoredTemplate>();
        var skipped = new List<SkippedTemplateFile>();

        if (!Directory.Exists(directory))
            return new Result(templates, skipped);

        // Ordered, so which of two clashing files is kept does not depend on the file system.
        var files = Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal);
        var keptKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in files)
        {
            var file = Path.GetFileName(path);

            var (stored, problem) = await TryReadAsync(path, cancellationToken);
            if (stored is null)
            {
                skipped.Add(problem!);
                continue;
            }

            if (Validate(file, stored) is { } invalid)
            {
                skipped.Add(invalid);
                continue;
            }

            if (keptKeys.TryGetValue(stored.Key!, out var keptFile))
            {
                skipped.Add(new SkippedTemplateFile(
                    file, SkipReasons.DuplicateKey, $"The same key as '{keptFile}', ignoring letter case."));
                continue;
            }

            keptKeys[stored.Key!] = file;
            templates.Add(AuthoredTemplate.Restore(
                stored.Key!,
                stored.Draft!.ToDomain(),
                stored.Published?.ToDomain(),
                stored.Revision,
                stored.CreatedAt,
                stored.UpdatedAt,
                stored.PublishedAt));
        }

        return new Result(templates, skipped);
    }

    private static async Task<(StoredTemplate? Stored, SkippedTemplateFile? Problem)> TryReadAsync(
        string path, CancellationToken cancellationToken)
    {
        var file = Path.GetFileName(path);

        try
        {
            await using var stream = File.OpenRead(path);
            var stored = await JsonSerializer.DeserializeAsync<StoredTemplate>(stream, Json, cancellationToken);

            return stored is null
                ? (null, new SkippedTemplateFile(file, SkipReasons.Malformed, "The file is empty or contains only null."))
                : (stored, null);
        }
        catch (JsonException exception)
        {
            return (null, new SkippedTemplateFile(file, SkipReasons.Malformed, exception.Message));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, new SkippedTemplateFile(file, SkipReasons.Unreadable, exception.Message));
        }
    }

    /// <summary>What makes a parsed file unusable, or null when it is fine.</summary>
    private static SkippedTemplateFile? Validate(string file, StoredTemplate stored)
    {
        if (!TemplateKey.IsValid(stored.Key))
            return new SkippedTemplateFile(file, SkipReasons.InvalidKey, $"'{stored.Key}' is not a valid key: {TemplateKey.Rule}.");

        // The old store found a template by its file name, so a file whose key says otherwise was
        // never reachable under either name.
        if (!string.Equals(Path.GetFileNameWithoutExtension(file), stored.Key, StringComparison.Ordinal))
            return new SkippedTemplateFile(file, SkipReasons.InvalidKey, $"The key inside the file is '{stored.Key}', which does not match the file name.");

        if (stored.Draft is null)
            return new SkippedTemplateFile(file, SkipReasons.Malformed, "The file has no draft.");

        if (stored.Revision < 1)
            return new SkippedTemplateFile(file, SkipReasons.Malformed, $"Revision {stored.Revision} is not valid; revisions start at 1.");

        return null;
    }
}
