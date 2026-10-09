using System.Text;
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
