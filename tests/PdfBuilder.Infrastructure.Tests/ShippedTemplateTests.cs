using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PdfBuilder.Infrastructure.Rendering;
using PdfBuilder.Infrastructure.Templates;

namespace PdfBuilder.Infrastructure.Tests;

/// <summary>
/// Renders the templates that ship in <c>templates/</c> with the sample payloads that ship beside
/// them. Unlike the rest of the suite this uses no fixtures, so it catches the case where the
/// documented example stops working - a broken sample is the first thing a newcomer hits.
/// </summary>
[Trait("Category", "Browser")]
public sealed class ShippedTemplateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Walks up from the test binaries to the repository root.</summary>
    private static string TemplatesDirectory
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "templates");
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "invoice.html")))
                    return candidate;
            }

            throw new DirectoryNotFoundException("Could not locate the repository's templates/ directory.");
        }
    }

    [Fact]
    public async Task The_sample_payload_is_a_json_object_the_validator_accepts()
    {
        var payload = await File.ReadAllTextAsync(
            Path.Combine(TemplatesDirectory, "invoice.sample.json"), Ct);

        // No browser needed for this half, so it also guards the sample when browser tests are off.
        var engine = new ScribanTemplateEngine(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new TemplateEngineOptions { StrictMode = true }));

        var repository = new FileSystemTemplateRepository(
            Options.Create(new TemplateStoreOptions { RootPath = TemplatesDirectory }),
            NullLogger<FileSystemTemplateRepository>.Instance);

        var template = await repository.GetByIdAsync("invoice", Ct);
        Assert.NotNull(template);

        // Strict mode on purpose: the shipped sample must populate every field the template reads,
        // otherwise the example teaches the wrong payload shape.
        var html = await engine.RenderAsync(template.Html, payload, Ct);

        Assert.Contains("Acme Trading LLC", html, StringComparison.Ordinal);
        Assert.Contains("INV-2026-0041", html, StringComparison.Ordinal);
        Assert.Contains("2,736.00", html, StringComparison.Ordinal);   // total, invariant formatting
        Assert.Contains("Onboarding workshop", html, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", html, StringComparison.Ordinal);   // nothing left unbound
    }

    /// <summary>Every payload in <c>samples/</c>, so a documented example cannot quietly rot.</summary>
    public static TheoryData<string> SamplePayloads
    {
        get
        {
            var root = Directory.GetParent(TemplatesDirectory)!.FullName;
            var data = new TheoryData<string>();

            foreach (var file in Directory.GetFiles(Path.Combine(root, "samples"), "*.json"))
                data.Add(Path.GetFileName(file));

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(SamplePayloads))]
    public async Task Every_sample_payload_renders_against_the_invoice_template(string fileName)
    {
        var root = Directory.GetParent(TemplatesDirectory)!.FullName;
        var payload = await File.ReadAllTextAsync(Path.Combine(root, "samples", fileName), Ct);

        var repository = new FileSystemTemplateRepository(
            Options.Create(new TemplateStoreOptions { RootPath = TemplatesDirectory }),
            NullLogger<FileSystemTemplateRepository>.Instance);

        var engine = new ScribanTemplateEngine(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new TemplateEngineOptions()));

        var template = await repository.GetByIdAsync("invoice", Ct);
        var html = await engine.RenderAsync(template!.Html, payload, Ct);

        Assert.Contains("</html>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_invoice_template_renders_to_a_pdf()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("PDFBUILDER_RUN_BROWSER_TESTS") != "1",
            "Set PDFBUILDER_RUN_BROWSER_TESTS=1 to run tests that launch Chromium.");

        var payload = await File.ReadAllTextAsync(
            Path.Combine(TemplatesDirectory, "invoice.sample.json"), Ct);

        var repository = new FileSystemTemplateRepository(
            Options.Create(new TemplateStoreOptions { RootPath = TemplatesDirectory }),
            NullLogger<FileSystemTemplateRepository>.Instance);

        var engine = new ScribanTemplateEngine(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new TemplateEngineOptions()));

        var template = await repository.GetByIdAsync("invoice", Ct);
        var html = await engine.RenderAsync(template!.Html, payload, Ct);

        await using var renderer = new PlaywrightPdfRenderer(
            Options.Create(new PlaywrightOptions { MaxConcurrentRenders = 1 }),
            NullLogger<PlaywrightPdfRenderer>.Instance);

        var pdf = await renderer.RenderAsync(html, template.Options, Ct);

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));

        // A blank A4 page is roughly 1 KB. Anything near that means the content did not make it in.
        Assert.True(pdf.Length > 5_000, $"PDF is suspiciously small at {pdf.Length} bytes.");
    }
}
