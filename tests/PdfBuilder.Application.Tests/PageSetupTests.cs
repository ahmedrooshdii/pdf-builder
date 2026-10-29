using PdfBuilder.Application.Features.GeneratePdf;
using PdfBuilder.Application.Features.Templates;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Application.Tests;

public sealed class PageSetupTests
{
    private static readonly PdfRenderOptions Template = PdfRenderOptions.Default with
    {
        Format = "A4",
        Landscape = true,
        PrintBackground = false,
        Margins = new PageMargins("0mm", "0mm", "0mm", "0mm")
    };

    // ---- Merging onto the template ----------------------------------------------------------

    [Fact]
    public void An_empty_setup_changes_nothing()
    {
        Assert.Equal(Template, new PageSetup().ApplyTo(Template));
    }

    [Fact]
    public void Keeps_everything_the_request_does_not_mention()
    {
        var applied = new PageSetup { Landscape = false }.ApplyTo(Template);

        Assert.False(applied.Landscape);
        Assert.Equal("A4", applied.Format);
        Assert.False(applied.PrintBackground);
        Assert.Equal(Template.Margins, applied.Margins);
        Assert.True(applied.PreferCssPageSize);
    }

    [Fact]
    public void A_custom_size_replaces_the_format()
    {
        var applied = new PageSetup { Width = "80mm", Height = "200mm" }.ApplyTo(Template);

        Assert.Equal("80mm", applied.Width);
        Assert.Equal("200mm", applied.Height);
    }

    [Fact]
    public void A_format_replaces_a_custom_size_from_the_template()
    {
        var receipt = Template with { Width = "80mm", Height = "200mm" };

        var applied = new PageSetup { Format = "Letter" }.ApplyTo(receipt);

        Assert.Equal("Letter", applied.Format);
        Assert.Null(applied.Width);
        Assert.Null(applied.Height);
    }

    [Fact]
    public void Asking_for_a_size_stops_the_templates_page_rule_from_overriding_it()
    {
        Assert.False(new PageSetup { Format = "A5" }.ApplyTo(Template).PreferCssPageSize);
        Assert.True(new PageSetup { Format = "A5", PreferCssPageSize = true }.ApplyTo(Template).PreferCssPageSize);
    }

    [Fact]
    public void Margins_change_side_by_side()
    {
        var applied = new PageSetup { Margins = new PageMarginsSetup(Top: "15mm") }.ApplyTo(Template);

        Assert.Equal(new PageMargins("15mm", "0mm", "0mm", "0mm"), applied.Margins);
    }

    // ---- Validating a request's setup -------------------------------------------------------

    private readonly GeneratePdfCommandValidator _command = new();

    private bool IsValid(PageSetup setup) => _command.Validate(new GeneratePdfCommand("invoice", "{}", setup)).IsValid;

    [Theory]
    [InlineData("A4")]
    [InlineData("a5")]
    [InlineData("Letter")]
    public void Accepts_known_formats(string format) => Assert.True(IsValid(new PageSetup { Format = format }));

    [Theory]
    [InlineData("")]
    [InlineData("B5")]
    [InlineData("80mm")]
    public void Rejects_unknown_formats(string format) => Assert.False(IsValid(new PageSetup { Format = format }));

    [Theory]
    [InlineData("80mm", "297mm")]
    [InlineData("3.15in", "8in")]
    [InlineData("8cm", "20cm")]
    [InlineData("302px", "600px")]
    public void Accepts_custom_sizes(string width, string height) =>
        Assert.True(IsValid(new PageSetup { Width = width, Height = height }));

    [Theory]
    [InlineData("80", "200mm")]          // no unit
    [InlineData("0mm", "200mm")]         // zero
    [InlineData("-80mm", "200mm")]       // negative
    [InlineData("80 mm", "200mm")]       // space
    [InlineData("80mm", "9000mm")]       // absurd
    [InlineData("calc(1px)", "200mm")]   // anything clever
    public void Rejects_malformed_lengths(string width, string height) =>
        Assert.False(IsValid(new PageSetup { Width = width, Height = height }));

    [Theory]
    [InlineData("auto")]
    [InlineData("AUTO")]
    public void Accepts_an_auto_height(string height) =>
        Assert.True(IsValid(new PageSetup { Width = "80mm", Height = height }));

    [Fact]
    public void Rejects_an_auto_width() => Assert.False(IsValid(new PageSetup { Width = "auto", Height = "200mm" }));

    [Fact]
    public void Rejects_an_auto_height_without_a_width() => Assert.False(IsValid(new PageSetup { Height = "auto" }));

    [Fact]
    public void Rejects_a_width_without_a_height() => Assert.False(IsValid(new PageSetup { Width = "80mm" }));

    [Fact]
    public void Rejects_a_format_and_a_custom_size_together() =>
        Assert.False(IsValid(new PageSetup { Format = "A4", Width = "80mm", Height = "200mm" }));

    [Fact]
    public void Rejects_malformed_margins() =>
        Assert.False(IsValid(new PageSetup { Margins = new PageMarginsSetup(Left: "wide") }));

    [Fact]
    public void Accepts_zero_margins() =>
        Assert.True(IsValid(new PageSetup { Margins = new PageMarginsSetup("0mm", "0mm", "0mm", "0mm") }));

    // ---- Validating a template's setup ------------------------------------------------------

    private static bool IsValidTemplate(PdfRenderOptions options) =>
        new TemplateContentValidator().Validate(new TemplateContent("<p>x</p>", "", "", options)).IsValid;

    [Fact]
    public void A_template_may_carry_a_custom_size() =>
        Assert.True(IsValidTemplate(PdfRenderOptions.Default with { Width = "80mm", Height = "250mm" }));

    [Fact]
    public void A_template_needs_both_dimensions() =>
        Assert.False(IsValidTemplate(PdfRenderOptions.Default with { Height = "250mm" }));

    [Fact]
    public void A_template_needs_a_known_format() =>
        Assert.False(IsValidTemplate(PdfRenderOptions.Default with { Format = "Napkin" }));
}
