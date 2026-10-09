namespace PdfBuilder.Domain.Templates;

/// <summary>
/// Per-template page setup. Supplied alongside the template (see the repository implementation),
/// so changing an invoice to landscape is a template change rather than a code change.
/// </summary>
public sealed record PdfRenderOptions
{
    public static PdfRenderOptions Default { get; } = new();

    /// <summary>A named paper size the browser understands, for example <c>A4</c>, <c>Letter</c>.</summary>
    public string Format { get; init; } = "A4";

    public bool Landscape { get; init; }

    /// <summary>When false, background colours and images are dropped from the output.</summary>
    public bool PrintBackground { get; init; } = true;

    /// <summary>When true, a CSS <c>@page { size: … }</c> rule in the template wins over <see cref="Format"/>.</summary>
    public bool PreferCssPageSize { get; init; } = true;

    public PageMargins Margins { get; init; } = PageMargins.Default;
}
