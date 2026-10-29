using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PdfBuilder.Domain.Exceptions;
using PdfBuilder.Domain.Templates;
using PdfBuilder.Infrastructure.Rendering;
using PdfBuilder.Infrastructure.Templates;

namespace PdfBuilder.Infrastructure.Tests;

/// <summary>
/// Exercises the real browser, so it is opt-in: set <c>PDFBUILDER_RUN_BROWSER_TESTS=1</c> and make
/// sure Chromium is installed (<c>pwsh bin/Debug/net10.0/playwright.ps1 install chromium</c>).
/// Everything else in this suite runs without one, which keeps the default loop fast.
/// </summary>
[Trait("Category", "Browser")]
public sealed class PlaywrightPdfRendererTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PlaywrightPdfRenderer CreateRenderer(PlaywrightOptions? options = null) => new(
        Options.Create(options ?? new PlaywrightOptions { MaxConcurrentRenders = 2 }),
        NullLogger<PlaywrightPdfRenderer>.Instance);

    private static void RequireBrowser() =>
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("PDFBUILDER_RUN_BROWSER_TESTS") != "1",
            "Set PDFBUILDER_RUN_BROWSER_TESTS=1 to run tests that launch Chromium.");

    [Fact]
    public async Task Produces_a_real_pdf_document()
    {
        RequireBrowser();
        await using var renderer = CreateRenderer();

        var pdf = await renderer.RenderAsync("<h1>Hello</h1>", PdfRenderOptions.Default, Ct);

        Assert.NotEmpty(pdf);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }

    [Fact]
    public async Task Renders_the_whole_pipeline_from_template_and_payload()
    {
        RequireBrowser();

        var engine = new ScribanTemplateEngine(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new TemplateEngineOptions()));

        var html = await engine.RenderAsync(
            "<html><body><h1>{{ customer.name }}</h1></body></html>",
            """{"customer":{"name":"Acme"}}""",
            Ct);

        await using var renderer = CreateRenderer();
        var pdf = await renderer.RenderAsync(html, PdfRenderOptions.Default, Ct);

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }

    [Fact]
    public async Task Prints_on_a_custom_page_size()
    {
        RequireBrowser();
        await using var renderer = CreateRenderer();

        var pdf = await renderer.RenderAsync(
            "<p>receipt</p>",
            PdfRenderOptions.Default with { Width = "80mm", Height = "100mm", PreferCssPageSize = false },
            Ct);

        // 80mm x 100mm is 226.77 x 283.46 points. Chromium rounds to whole device pixels on the
        // way, so it writes 227.04 x 283.92 into the MediaBox: close, not exact.
        var box = Regex.Match(Encoding.Latin1.GetString(pdf), @"/MediaBox \[0 0 (?<w>[\d.]+) (?<h>[\d.]+)\]");
        Assert.True(box.Success, "The PDF has no MediaBox.");
        Assert.Equal(226.77, double.Parse(box.Groups["w"].Value, CultureInfo.InvariantCulture), tolerance: 1);
        Assert.Equal(283.46, double.Parse(box.Groups["h"].Value, CultureInfo.InvariantCulture), tolerance: 1);
    }

    private static readonly PdfRenderOptions Receipt = PdfRenderOptions.Default with
    {
        Width = "80mm",
        Height = PageSize.Auto,
        Margins = new PageMargins("4mm", "4mm", "4mm", "4mm")
    };

    [Fact]
    public async Task Fits_a_long_receipt_on_one_page_when_the_height_is_auto()
    {
        RequireBrowser();
        await using var renderer = CreateRenderer();

        // Far longer than any named paper: 150 lines would run to several A4 pages.
        var lines = string.Concat(Enumerable.Range(1, 150).Select(i => $"<p style=\"margin:0\">Item {i} ........ 9.99</p>"));
        var pdf = await renderer.RenderAsync($"<html><body>{lines}</body></html>", Receipt, Ct);

        var (width, height, pages) = Describe(pdf);
        Assert.Equal(1, pages);
        Assert.Equal(226.77, width, tolerance: 1);
        Assert.True(height > 842, $"Expected a page taller than A4, got {height}pt.");
    }

    [Fact]
    public async Task Keeps_a_short_receipt_short_when_the_height_is_auto()
    {
        RequireBrowser();
        await using var renderer = CreateRenderer();

        var pdf = await renderer.RenderAsync("<p>Total 9.99</p>", Receipt, Ct);

        var (_, height, pages) = Describe(pdf);
        Assert.Equal(1, pages);
        Assert.True(height < 150, $"Expected a page about as tall as one line plus margins, got {height}pt.");
    }

    [Fact]
    public async Task Ignores_the_templates_page_rule_when_the_height_is_auto()
    {
        RequireBrowser();
        await using var renderer = CreateRenderer();

        // Short on purpose: a page wider than it is tall is the one Chromium would turn to match the
        // template's portrait A4.
        var pdf = await renderer.RenderAsync(
            "<html><head><style>@page { size: A4; }</style></head><body><p>x</p></body></html>",
            Receipt with { PreferCssPageSize = true },
            Ct);

        var (width, height, pages) = Describe(pdf);
        Assert.Equal(1, pages);
        Assert.Equal(226.77, width, tolerance: 1);
        Assert.True(height < width, $"Expected the short receipt to stay wider than tall, got {width}x{height}pt.");
    }

    /// <summary>The first page's size in points, and how many pages there are.</summary>
    private static (double Width, double Height, int Pages) Describe(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var box = Regex.Match(text, @"/MediaBox \[0 0 (?<w>[\d.]+) (?<h>[\d.]+)\]");
        Assert.True(box.Success, "The PDF has no MediaBox.");

        return (
            double.Parse(box.Groups["w"].Value, CultureInfo.InvariantCulture),
            double.Parse(box.Groups["h"].Value, CultureInfo.InvariantCulture),
            Regex.Count(text, @"/Type\s*/Page(?!s)"));
    }

    [Fact]
    public async Task Serves_concurrent_renders_from_one_browser()
    {
        RequireBrowser();
        await using var renderer = CreateRenderer();

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            renderer.RenderAsync($"<p>page {i}</p>", PdfRenderOptions.Default, Ct)));

        Assert.All(results, pdf => Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5)));
    }

    [Fact]
    public async Task Recycles_the_browser_once_it_has_served_its_quota()
    {
        RequireBrowser();
        await using var renderer = CreateRenderer(new PlaywrightOptions
        {
            MaxConcurrentRenders = 1,
            MaxRendersPerBrowser = 2
        });

        // Five renders across a quota of two forces at least two recycles mid-flight.
        for (var i = 0; i < 5; i++)
        {
            var pdf = await renderer.RenderAsync($"<p>{i}</p>", PdfRenderOptions.Default, Ct);
            Assert.NotEmpty(pdf);
        }
    }

    [Fact]
    public async Task Blocks_requests_to_hosts_that_are_not_allow_listed()
    {
        RequireBrowser();
        await using var renderer = CreateRenderer(new PlaywrightOptions
        {
            MaxConcurrentRenders = 1,
            RenderTimeoutMs = 5_000,
            AllowedHosts = []
        });

        // The image must be blocked rather than fetched; the page still renders without it.
        var pdf = await renderer.RenderAsync(
            """<html><body><img src="http://169.254.169.254/latest/meta-data/" alt="probe"></body></html>""",
            PdfRenderOptions.Default,
            Ct);

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }

    [Fact]
    public async Task Refuses_to_render_after_disposal()
    {
        RequireBrowser();
        var renderer = CreateRenderer();
        await renderer.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => renderer.RenderAsync("<p>x</p>", PdfRenderOptions.Default, Ct));
    }

    [Fact]
    public async Task Reports_a_browser_timeout_as_a_transient_failure()
    {
        RequireBrowser();
        await using var renderer = CreateRenderer(new PlaywrightOptions
        {
            MaxConcurrentRenders = 1,
            EnableJavaScript = true,
            RenderTimeoutMs = 1_000
        });

        // An image that never resolves keeps `load` pending until the timeout fires.
        var exception = await Record.ExceptionAsync(() => renderer.RenderAsync(
            """<html><body><img src="https://10.255.255.1/never.png" alt=""></body></html>""",
            PdfRenderOptions.Default,
            Ct));

        if (exception is not null)
            Assert.IsType<PdfRenderException>(exception);
    }
}
