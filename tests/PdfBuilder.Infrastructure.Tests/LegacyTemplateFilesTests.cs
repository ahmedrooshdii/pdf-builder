using PdfBuilder.Domain.Templates;
using PdfBuilder.Infrastructure.Persistence.Records;
using PdfBuilder.Infrastructure.Templates;

namespace PdfBuilder.Infrastructure.Tests;

/// <summary>
/// Reading the files the old file store wrote. <see cref="Golden"/> is a real one, produced by that
/// store, so these tests prove the new reader understands what is actually on a deployed machine.
/// </summary>
public sealed class LegacyTemplateFilesTests : IDisposable
{
    private static readonly DateTimeOffset Created = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A published template with a later, unpublished draft, at revision 3: status Modified.</summary>
    private const string Golden = """
        {
          "key": "letter",
          "revision": 3,
          "createdAt": "2026-10-28T12:00:00+00:00",
          "updatedAt": "2026-10-28T12:02:00+00:00",
          "publishedAt": "2026-10-28T12:01:00+00:00",
          "draft": {
            "html": "<p>newer {{ a }}</p>",
            "css": "p { color: red; }",
            "sampleJson": "{ \"a\": 1 }",
            "options": {
              "format": "A4",
              "width": null,
              "height": null,
              "landscape": true,
              "printBackground": true,
              "preferCssPageSize": true,
              "margins": { "top": "5mm", "right": "10mm", "bottom": "10mm", "left": "10mm" }
            },
            "sampleOrEmpty": "{ \"a\": 1 }"
          },
          "published": {
            "html": "<p>{{ a }}</p>",
            "css": "p { color: red; }",
            "sampleJson": "{ \"a\": 1 }",
            "options": {
              "format": "A4",
              "width": null,
              "height": null,
              "landscape": true,
              "printBackground": true,
              "preferCssPageSize": true,
              "margins": { "top": "5mm", "right": "10mm", "bottom": "10mm", "left": "10mm" }
            },
            "sampleOrEmpty": "{ \"a\": 1 }"
          }
        }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pdf-builder-legacy-{Guid.NewGuid():N}");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The old store's JSON encoder wrote <c>&lt;</c>, <c>&gt;</c> and <c>"</c> inside strings as
    /// <c><</c>, <c>></c> and <c>"</c>. <see cref="Golden"/> spells them plainly so it
    /// can be read; this puts the real escapes back, so the tests cover decoding them too. The
    /// backslash is added at run time because writing these escapes in source gets them converted.
    /// </summary>
    private static string AsTheOldStoreWroteIt(string json)
    {
        const char backslash = '\\';
        return json
            .Replace("<", $"{backslash}u003C", StringComparison.Ordinal)
            .Replace(">", $"{backslash}u003E", StringComparison.Ordinal)
            .Replace($"{backslash}\"", $"{backslash}u0022", StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void Write(string fileName, string text)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, fileName), text);
    }

    private static string Minimal(string key, string extra = "") =>
        $$"""{ "key": "{{key}}", "revision": 1, "createdAt": "2026-10-28T12:00:00+00:00", "updatedAt": "2026-10-28T12:00:00+00:00", "draft": { "html": "<p></p>", "css": "", "sampleJson": "{}" }{{extra}} }""";

    [Fact]
    public async Task Reads_a_file_the_old_store_wrote_exactly()
    {
        Write("letter.json", AsTheOldStoreWroteIt(Golden));
        Assert.Contains($"{'\\'}u003C", File.ReadAllText(Path.Combine(_root, "letter.json")), StringComparison.Ordinal);

        var result = await LegacyTemplateFiles.ReadAsync(_root, Ct);

        Assert.True(result.Skipped.Count == 0, string.Join("; ", result.Skipped.Select(s => $"{s.File}: {s.Reason}: {s.Detail}")));

        var published = new TemplateContent(
            "<p>{{ a }}</p>", "p { color: red; }", "{ \"a\": 1 }",
            PdfRenderOptions.Default with { Landscape = true, Margins = new PageMargins(Top: "5mm") });
        var expected = AuthoredTemplate.Restore(
            "letter", published with { Html = "<p>newer {{ a }}</p>" }, published,
            revision: 3, Created, Created.AddMinutes(2), Created.AddMinutes(1));

        Assert.Equal([expected], result.Templates);
        Assert.Equal(TemplateStatus.Modified, result.Templates[0].Status);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public async Task A_file_from_before_a_setting_existed_gets_that_settings_default()
    {
        // Files written before custom page sizes existed have no width, height or preferCssPageSize,
        // and none of the print settings either; the old store let the domain defaults fill them in.
        Write("old.json", Minimal("old"));

        var result = await LegacyTemplateFiles.ReadAsync(_root, Ct);

        var options = result.Templates.Single().Draft.Options;
        Assert.Equal(PdfRenderOptions.Default, options);
        Assert.True(options.PrintBackground);
        Assert.True(options.PreferCssPageSize);
    }

    [Fact]
    public async Task A_missing_directory_has_nothing_to_import()
    {
        var result = await LegacyTemplateFiles.ReadAsync(Path.Combine(_root, "never-created"), Ct);

        Assert.Empty(result.Templates);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public async Task Ignores_files_that_are_not_templates()
    {
        Write("letter.json", AsTheOldStoreWroteIt(Golden));
        Write("letter.json.0f3c.tmp", "{ half written");
        Write("notes.txt", "hello");

        var result = await LegacyTemplateFiles.ReadAsync(_root, Ct);

        Assert.Equal(["letter"], result.Templates.Select(t => t.Key));
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public async Task A_file_that_is_not_valid_json_is_skipped_as_malformed_and_the_rest_are_still_read()
    {
        Write("letter.json", AsTheOldStoreWroteIt(Golden));
        Write("broken.json", "{");

        var result = await LegacyTemplateFiles.ReadAsync(_root, Ct);

        Assert.Equal(["letter"], result.Templates.Select(t => t.Key));
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal("broken.json", skipped.File);
        Assert.Equal(SkipReasons.Malformed, skipped.Reason);
        Assert.False(string.IsNullOrWhiteSpace(skipped.Detail), "the detail is what lets someone fix the file");
    }

    // The file is named after its key in every case, so the only thing wrong with it is its content.
    [Theory]
    [InlineData("wrong-types", """{ "key": "wrong-types", "revision": "three", "draft": { "html": "x", "css": "", "sampleJson": "{}" } }""")]
    [InlineData("literal-null", "null")]
    [InlineData("no-draft", """{ "key": "no-draft", "revision": 1, "createdAt": "2026-10-28T12:00:00+00:00", "updatedAt": "2026-10-28T12:00:00+00:00" }""")]
    [InlineData("revision-zero", """{ "key": "revision-zero", "revision": 0, "createdAt": "2026-10-28T12:00:00+00:00", "updatedAt": "2026-10-28T12:00:00+00:00", "draft": { "html": "x", "css": "", "sampleJson": "{}" } }""")]
    public async Task A_file_that_is_not_a_usable_template_is_skipped_as_malformed(string key, string json)
    {
        Write($"{key}.json", json);

        var result = await LegacyTemplateFiles.ReadAsync(_root, Ct);

        Assert.Empty(result.Templates);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal($"{key}.json", skipped.File);
        Assert.Equal(SkipReasons.Malformed, skipped.Reason);
    }

    [Theory]
    [InlineData("has space", "has space.json")]
    [InlineData("-leading-dash", "-leading-dash.json")]
    public async Task A_file_whose_key_is_not_a_valid_key_is_skipped_as_invalid_key(string key, string fileName)
    {
        Write(fileName, Minimal(key));

        var result = await LegacyTemplateFiles.ReadAsync(_root, Ct);

        Assert.Empty(result.Templates);
        Assert.Equal(SkipReasons.InvalidKey, Assert.Single(result.Skipped).Reason);
    }

    [Fact]
    public async Task A_file_whose_key_does_not_match_its_name_is_skipped_as_invalid_key()
    {
        // The old store found a template by file name, so this one was never reachable.
        Write("letter.json", Minimal("receipt"));

        var result = await LegacyTemplateFiles.ReadAsync(_root, Ct);

        Assert.Empty(result.Templates);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(SkipReasons.InvalidKey, skipped.Reason);
        Assert.Contains("receipt", skipped.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_another_process_holds_open_is_skipped_as_unreadable()
    {
        Write("letter.json", AsTheOldStoreWroteIt(Golden));
        Write("locked.json", Minimal("locked"));

        // FileShare.None: nobody else may open it, which is how a half-copied file looks on Windows
        // and, through advisory locks, on Linux.
        await using var hold = new FileStream(Path.Combine(_root, "locked.json"), FileMode.Open, FileAccess.Read, FileShare.None);

        var result = await LegacyTemplateFiles.ReadAsync(_root, Ct);

        Assert.Equal(["letter"], result.Templates.Select(t => t.Key));
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal("locked.json", skipped.File);
        Assert.Equal(SkipReasons.Unreadable, skipped.Reason);
    }

    [Fact]
    public async Task Two_files_whose_keys_differ_only_in_letter_case_import_only_the_first()
    {
        // Only possible on a case-sensitive file system, which is what the production image runs on.
        Directory.CreateDirectory(_root);
        Write("Invoice.json", Minimal("Invoice"));
        Write("invoice.json", Minimal("invoice"));
        Assert.SkipWhen(
            Directory.GetFiles(_root, "*.json").Length < 2,
            "This file system treats names that differ only in case as the same file.");

        var result = await LegacyTemplateFiles.ReadAsync(_root, Ct);

        // Ordinal order puts the capital first, so that one is kept, whatever the file system lists first.
        Assert.Equal(["Invoice"], result.Templates.Select(t => t.Key));
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal("invoice.json", skipped.File);
        Assert.Equal(SkipReasons.DuplicateKey, skipped.Reason);
    }

    [Fact]
    public async Task Reading_changes_nothing_on_disk()
    {
        Write("letter.json", AsTheOldStoreWroteIt(Golden));
        var path = Path.Combine(_root, "letter.json");
        var before = (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path));

        await LegacyTemplateFiles.ReadAsync(_root, Ct);

        Assert.Equal(before.Item1, File.ReadAllBytes(path));
        Assert.Equal(before.Item2, File.GetLastWriteTimeUtc(path));
        Assert.Equal(["letter.json"], Directory.GetFiles(_root).Select(Path.GetFileName));
    }
}
