namespace PdfBuilder.Domain.Templates;

/// <summary>
/// Per-template page setup. Supplied alongside the template (see the repository implementation),
/// so changing an invoice to landscape is a template change rather than a code change. A request
/// can adjust it for one job with a <see cref="PageSetup"/>.
/// </summary>
public sealed record PdfRenderOptions
{
    public static PdfRenderOptions Default { get; } = new();

    /// <summary>A named paper size, one of <see cref="PageSize.Formats"/>. Ignored when <see cref="Width"/> and <see cref="Height"/> are set.</summary>
    public string Format { get; init; } = "A4";

    /// <summary>
    /// Custom page width as a CSS length (<c>80mm</c>, <c>4in</c>), for paper no named format
    /// describes: receipts, labels, tickets. Set together with <see cref="Height"/>.
    /// </summary>
    public string? Width { get; init; }

    /// <summary>
    /// Custom page height as a CSS length, or <see cref="PageSize.Auto"/> for a single page as tall
    /// as the content. Set together with <see cref="Width"/>.
    /// </summary>
    public string? Height { get; init; }

    public bool Landscape { get; init; }

    /// <summary>When false, background colours and images are dropped from the output.</summary>
    public bool PrintBackground { get; init; } = true;

    /// <summary>
    /// When true, a CSS <c>@page { size: … }</c> rule in the template wins over <see cref="Format"/>
    /// and the custom size.
    /// </summary>
    public bool PreferCssPageSize { get; init; } = true;

    public PageMargins Margins { get; init; } = PageMargins.Default;
}
