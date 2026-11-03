using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Templates;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.Infrastructure.Persistence.Records;
using PdfBuilder.Infrastructure.Templates;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Infrastructure.Tests;

/// <summary>
/// The one-time carry-over of the old template files, against a real PostgreSQL: exactly once, even
/// when instances start together; files left alone; bad files reported instead of stopping the rest.
/// </summary>
public sealed class TemplateImporterTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pdf-builder-import-{Guid.NewGuid():N}");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

    /// <summary>A file in the old format. Published templates also carry their published content.</summary>
    private static string Template(string key, bool published = false)
    {
        const string content = """{ "html": "<p>x</p>", "css": "", "sampleJson": "{}" }""";
        var publishedAt = published ? "\"2026-10-28T12:01:00+00:00\"" : "null";

        return $$"""
            { "key": "{{key}}", "revision": {{(published ? 2 : 1)}},
              "createdAt": "2026-10-28T12:00:00+00:00", "updatedAt": "2026-10-28T12:00:00+00:00",
              "publishedAt": {{publishedAt}},
              "draft": {{content}}, "published": {{(published ? content : "null")}} }
            """;
    }

    private static IBuiltInTemplateSource BuiltIns(params string[] keys)
    {
        IReadOnlyList<BuiltInTemplate> templates =
        [
            .. keys.Select(key => new BuiltInTemplate(key, new TemplateContent("<p></p>", "", "{}", PdfRenderOptions.Default), Now))
        ];

        var source = Substitute.For<IBuiltInTemplateSource>();
        source.ListAsync(Arg.Any<CancellationToken>()).Returns(templates);
        return source;
    }

    private TemplateImporter Importer(params string[] builtInKeys) => new(
        Options.Create(new TemplateStoreOptions { RootPath = "unused", AuthoredRootPath = _root }),
        BuiltIns(builtInKeys),
        new TestClock(Now),
        NullLogger<TemplateImporter>.Instance);

    private static async Task RunAsync(TemplateImporter importer, TestDatabase database)
    {
        await using var db = database.CreateContext();
        await importer.ImportAsync(db, Ct);
    }

    private static async Task<TemplateImportRecord?> ImportRecordAsync(TestDatabase database)
    {
        await using var db = database.CreateContext();
        return await db.Set<TemplateImportRecord>().AsNoTracking().SingleOrDefaultAsync(Ct);
    }

    private static async Task<IReadOnlyList<string>> KeysAsync(TestDatabase database) =>
        [.. (await new EfAuthoredTemplateStore(database.CreateContext()).ListAsync(Ct)).Select(t => t.Key)];

    [Fact]
    public async Task The_first_run_imports_every_readable_file_and_reports_the_rest()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        Write("letter.json", Template("letter", published: true));
        Write("receipt.json", Template("receipt"));
        Write("broken.json", "{");

        await RunAsync(Importer(), database);

        Assert.Equal(["letter", "receipt"], await KeysAsync(database));
        var letter = await new EfAuthoredTemplateStore(database.CreateContext()).GetAsync("letter", Ct);
        Assert.Equal(TemplateStatus.Published, letter!.Status);
        Assert.Equal(2, letter.Revision);

        var record = await ImportRecordAsync(database);
        Assert.Equal(2, record!.ImportedCount);
        Assert.Equal(_root, record.SourcePath);
        var skipped = Assert.Single(record.SkippedFiles);
        Assert.Equal("broken.json", skipped.File);
        Assert.Equal(SkipReasons.Malformed, skipped.Reason);
    }

    [Fact]
    public async Task A_file_whose_key_is_a_built_in_template_is_skipped_in_any_letter_case()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        Write("Invoice.json", Template("Invoice"));
        Write("letter.json", Template("letter"));

        await RunAsync(Importer(builtInKeys: "invoice"), database);

        Assert.Equal(["letter"], await KeysAsync(database));
        var skipped = Assert.Single((await ImportRecordAsync(database))!.SkippedFiles);
        Assert.Equal("Invoice.json", skipped.File);
        Assert.Equal(SkipReasons.BuiltInKey, skipped.Reason);
    }

    [Fact]
    public async Task Later_runs_import_nothing_even_when_the_files_are_still_there()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        Write("letter.json", Template("letter"));
        await RunAsync(Importer(), database);

        // After the upgrade the operator deletes the template and someone drops a new file in the old place.
        await new EfAuthoredTemplateStore(database.CreateContext()).DeleteAsync("letter", Ct);
        Write("late.json", Template("late"));
        await RunAsync(Importer(), database);

        Assert.Empty(await KeysAsync(database));
        Assert.Equal(1, (await ImportRecordAsync(database))!.ImportedCount);
    }

    [Fact]
    public async Task The_files_are_left_exactly_as_they_were_so_an_upgrade_can_be_rolled_back()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        Write("letter.json", Template("letter"));
        Write("broken.json", "{");
        var before = Snapshot();

        await RunAsync(Importer(), database);

        Assert.Equal(before, Snapshot());
    }

    // Content as hex: byte arrays inside a tuple compare by reference, which would make every snapshot differ.
    private List<(string Name, string ContentHex, DateTime Written)> Snapshot() =>
    [
        .. Directory.GetFiles(_root).Order(StringComparer.Ordinal)
            .Select(path => (Path.GetFileName(path), Convert.ToHexString(File.ReadAllBytes(path)), File.GetLastWriteTimeUtc(path)))
    ];

    [Fact]
    public async Task A_missing_directory_still_records_the_import_so_nothing_scans_again()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();

        await RunAsync(Importer(), database);

        Assert.Equal(0, (await ImportRecordAsync(database))!.ImportedCount);

        // The directory appears later, say a restored backup; it is not this deployment's import any more.
        Write("letter.json", Template("letter"));
        await RunAsync(Importer(), database);
        Assert.Empty(await KeysAsync(database));
    }

    [Fact]
    public async Task Instances_starting_at_the_same_time_import_each_template_exactly_once()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        for (var n = 0; n < 12; n++)
            Write($"template-{n:D2}.json", Template($"template-{n:D2}"));

        // Six starts, as if six instances came up together, each with its own context.
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => RunAsync(Importer(), database)));

        Assert.Equal(12, (await KeysAsync(database)).Count);
        Assert.Equal(12, (await ImportRecordAsync(database))!.ImportedCount);
    }
}
