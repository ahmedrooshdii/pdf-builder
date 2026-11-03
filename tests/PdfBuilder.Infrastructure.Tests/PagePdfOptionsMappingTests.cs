using PdfBuilder.Domain.Templates;
using PdfBuilder.Infrastructure.Rendering;

namespace PdfBuilder.Infrastructure.Tests;

/// <summary>The translation to Playwright, which needs no browser to check.</summary>
public sealed class PagePdfOptionsMappingTests
{
    [Fact]
    public void A_named_format_is_passed_through()
    {
        var options = PlaywrightPdfRenderer.ToPagePdfOptions(PdfRenderOptions.Default with { Format = "Letter" });

        Assert.Equal("Letter", options.Format);
        Assert.Null(options.Width);
        Assert.Null(options.Height);
    }

    [Fact]
    public void A_custom_size_leaves_the_format_out_because_playwright_would_prefer_it()
    {
        var options = PlaywrightPdfRenderer.ToPagePdfOptions(
            PdfRenderOptions.Default with { Width = "80mm", Height = "200mm" });

        Assert.Null(options.Format);
        Assert.Equal("80mm", options.Width);
        Assert.Equal("200mm", options.Height);
    }

    [Fact]
    public void An_auto_height_is_left_for_measuring_and_keeps_the_page_upright()
    {
        var options = PlaywrightPdfRenderer.ToPagePdfOptions(
            PdfRenderOptions.Default with { Width = "80mm", Height = "auto", Landscape = true, PreferCssPageSize = true });

        Assert.Null(options.Format);
        Assert.Equal("80mm", options.Width);
        Assert.Null(options.Height);
        Assert.False(options.Landscape);
        Assert.False(options.PreferCSSPageSize);
    }

    [Fact]
    public void Half_a_custom_size_falls_back_to_the_format()
    {
        var options = PlaywrightPdfRenderer.ToPagePdfOptions(PdfRenderOptions.Default with { Width = "80mm" });

        Assert.Equal("A4", options.Format);
        Assert.Null(options.Width);
    }
}
