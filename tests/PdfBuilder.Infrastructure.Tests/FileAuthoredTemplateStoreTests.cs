using Microsoft.Extensions.Options;
using PdfBuilder.Domain.Exceptions;
using PdfBuilder.Domain.Templates;
using PdfBuilder.Infrastructure.Templates;

namespace PdfBuilder.Infrastructure.Tests;

public sealed class FileAuthoredTemplateStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private static readonly TemplateContent Content = new(
        "<p>{{ a }}</p>",
        "p { color: red; }",
        "{ \"a\": 1 }",
        PdfRenderOptions.Default with { Landscape = true, Margins = new PageMargins(Top: "5mm") });

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pdf-builder-authored-{Guid.NewGuid():N}");
    private readonly FileAuthoredTemplateStore _store;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public FileAuthoredTemplateStoreTests()
    {
        _store = new FileAuthoredTemplateStore(
            Options.Create(new TemplateStoreOptions { RootPath = "unused", AuthoredRootPath = _root }));
    }

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Reads_back_exactly_what_was_stored()
    {
        var template = AuthoredTemplate.Create("letter", Content, Now).Publish(1, Now);

        await _store.AddAsync(template, Ct);

        Assert.Equal(template, await _store.GetAsync("letter", Ct));
        Assert.Equal(["letter"], (await _store.ListAsync(Ct)).Select(t => t.Key));
    }

    [Fact]
    public async Task Is_empty_before_anything_is_written()
    {
        Assert.Empty(await _store.ListAsync(Ct));
        Assert.Null(await _store.GetAsync("letter", Ct));
    }

    [Fact]
    public async Task Refuses_to_add_a_key_twice()
    {
        await _store.AddAsync(AuthoredTemplate.Create("letter", Content, Now), Ct);

        await Assert.ThrowsAsync<TemplateKeyTakenException>(() =>
            _store.AddAsync(AuthoredTemplate.Create("letter", Content, Now), Ct));
    }

    [Fact]
    public async Task Refuses_an_update_based_on_a_revision_that_is_no_longer_current()
    {
        var original = AuthoredTemplate.Create("letter", Content, Now);
        await _store.AddAsync(original, Ct);

        var first = original.SaveDraft(Content with { Html = "<p>one</p>" }, 1, Now);
        await _store.UpdateAsync(first, expectedRevision: 1, Ct);

        // A second editor who also loaded revision 1 must not overwrite the first editor's save.
        var second = original.SaveDraft(Content with { Html = "<p>two</p>" }, 1, Now);
        await Assert.ThrowsAsync<StaleTemplateRevisionException>(() => _store.UpdateAsync(second, expectedRevision: 1, Ct));

        Assert.Equal("<p>one</p>", (await _store.GetAsync("letter", Ct))!.Draft.Html);
    }

    [Fact]
    public async Task Leaves_no_temporary_files_behind()
    {
        var template = AuthoredTemplate.Create("letter", Content, Now);
        await _store.AddAsync(template, Ct);
        await _store.UpdateAsync(template.Publish(1, Now), 1, Ct);

        Assert.Equal(["letter.json"], Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Deletes_and_reports_whether_there_was_anything_to_delete()
    {
        await _store.AddAsync(AuthoredTemplate.Create("letter", Content, Now), Ct);

        Assert.True(await _store.DeleteAsync("letter", Ct));
        Assert.False(await _store.DeleteAsync("letter", Ct));
        Assert.Null(await _store.GetAsync("letter", Ct));
    }

    [Theory]
    [InlineData("../letter")]
    [InlineData("sub/letter")]
    public async Task Never_resolves_a_key_outside_its_directory(string key)
    {
        Assert.Null(await _store.GetAsync(key, Ct));
        Assert.False(await _store.DeleteAsync(key, Ct));
    }
}
