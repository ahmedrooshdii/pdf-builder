using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PdfBuilder.Domain.Templates;
using PdfBuilder.Infrastructure.Templates;

namespace PdfBuilder.Infrastructure.Tests;

public sealed class FileSystemTemplateRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pdf-builder-tpl-{Guid.NewGuid():N}");
    private readonly FileSystemTemplateRepository _repository;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public FileSystemTemplateRepositoryTests()
    {
        Directory.CreateDirectory(_root);
        _repository = new FileSystemTemplateRepository(
            Options.Create(new TemplateStoreOptions { RootPath = _root }),
            NullLogger<FileSystemTemplateRepository>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void WriteFile(string name, string content) => File.WriteAllText(Path.Combine(_root, name), content);

    [Fact]
    public async Task Loads_a_template_by_id()
    {
        WriteFile("invoice.html", "<p>hello</p>");

        var template = await _repository.GetByIdAsync("invoice", Ct);

        Assert.NotNull(template);
        Assert.Equal("invoice", template.Id);
        Assert.Equal("<p>hello</p>", template.Html);
    }

    [Fact]
    public async Task Uses_the_default_page_setup_when_no_sidecar_exists()
    {
        WriteFile("plain.html", "<p>x</p>");

        var template = await _repository.GetByIdAsync("plain", Ct);

        Assert.Equal(PdfRenderOptions.Default, template!.Options);
    }

    [Fact]
    public async Task Reads_page_setup_from_the_sidecar_file()
    {
        WriteFile("label.html", "<p>x</p>");
        WriteFile(
            "label.options.json",
            """{ "format": "Letter", "landscape": true, "margins": { "top": "5mm", "left": "6mm" } }""");

        var template = await _repository.GetByIdAsync("label", Ct);

        Assert.Equal("Letter", template!.Options.Format);
        Assert.True(template.Options.Landscape);
        Assert.Equal("5mm", template.Options.Margins.Top);
        Assert.Equal("6mm", template.Options.Margins.Left);

        // Members the sidecar does not mention keep their defaults.
        Assert.Equal("10mm", template.Options.Margins.Right);
        Assert.True(template.Options.PrintBackground);
    }

    [Fact]
    public async Task Falls_back_to_defaults_when_the_sidecar_is_malformed()
    {
        WriteFile("broken.html", "<p>x</p>");
        WriteFile("broken.options.json", "{ not json");

        var template = await _repository.GetByIdAsync("broken", Ct);

        Assert.NotNull(template);
        Assert.Equal(PdfRenderOptions.Default, template.Options);
    }

    [Fact]
    public async Task Returns_null_for_an_unknown_template()
    {
        Assert.Null(await _repository.GetByIdAsync("nope", Ct));
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("sub/invoice")]
    [InlineData("")]
    public async Task Refuses_ids_that_point_outside_the_template_directory(string templateId)
    {
        Assert.Null(await _repository.GetByIdAsync(templateId, Ct));
    }
}
