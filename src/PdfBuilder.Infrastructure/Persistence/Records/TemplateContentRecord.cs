using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Infrastructure.Persistence.Records;

/// <summary>
/// The stored shape of a template version, serialised as JSON. Its property names are the ones the
/// file store wrote (<c>html</c>, <c>css</c>, <c>sampleJson</c>, <c>options</c>), so the importer reads
/// the old files into this same type.
/// </summary>
/// <remarks>
/// Every setting is nullable and falls back to the domain default when missing. Files written by an
/// older version predate some settings (a custom page size, for one), and the file store let the
/// domain type's defaults fill those in; this keeps doing the same.
/// </remarks>
internal sealed record TemplateContentRecord(string Html, string Css, string SampleJson, RenderOptionsRecord? Options)
{
    public static TemplateContentRecord From(TemplateContent content) =>
        new(content.Html, content.Css, content.SampleJson, RenderOptionsRecord.From(content.Options));

    public TemplateContent ToDomain() =>
        new(Html ?? string.Empty, Css ?? string.Empty, SampleJson ?? string.Empty, Options?.ToDomain() ?? PdfRenderOptions.Default);
}

internal sealed record RenderOptionsRecord(
    string? Format = null,
    string? Width = null,
    string? Height = null,
    bool? Landscape = null,
    bool? PrintBackground = null,
    bool? PreferCssPageSize = null,
    MarginsRecord? Margins = null)
{
    public static RenderOptionsRecord From(PdfRenderOptions options) => new(
        options.Format,
        options.Width,
        options.Height,
        options.Landscape,
        options.PrintBackground,
        options.PreferCssPageSize,
        MarginsRecord.From(options.Margins));

    public PdfRenderOptions ToDomain()
    {
        var defaults = PdfRenderOptions.Default;

        return new PdfRenderOptions
        {
            Format = Format ?? defaults.Format,
            Width = Width,
            Height = Height,
            Landscape = Landscape ?? defaults.Landscape,
            PrintBackground = PrintBackground ?? defaults.PrintBackground,
            PreferCssPageSize = PreferCssPageSize ?? defaults.PreferCssPageSize,
            Margins = Margins?.ToDomain() ?? defaults.Margins
        };
    }
}

internal sealed record MarginsRecord(string? Top = null, string? Right = null, string? Bottom = null, string? Left = null)
{
    public static MarginsRecord From(PageMargins margins) => new(margins.Top, margins.Right, margins.Bottom, margins.Left);

    public PageMargins ToDomain()
    {
        var defaults = PageMargins.Default;
        return new PageMargins(Top ?? defaults.Top, Right ?? defaults.Right, Bottom ?? defaults.Bottom, Left ?? defaults.Left);
    }
}
