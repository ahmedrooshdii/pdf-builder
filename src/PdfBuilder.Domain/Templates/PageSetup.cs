namespace PdfBuilder.Domain.Templates;

/// <summary>
/// One request's adjustments to a template's page setup. Every member is optional, and only the
/// ones set change anything: a caller that wants the invoice on Letter paper sends
/// <c>Format = "Letter"</c> and keeps the template's margins, orientation and the rest.
/// </summary>
/// <remarks>
/// Size is all-or-nothing: <see cref="Format"/> replaces the template's size whatever kind it was,
/// and so do <see cref="Width"/> with <see cref="Height"/>, which travel together. A request that
/// sets a size also turns <see cref="PdfRenderOptions.PreferCssPageSize"/> off unless it says
/// otherwise, because a size someone asked for by name should not lose silently to an
/// <c>@page</c> rule in the template.
/// </remarks>
public sealed record PageSetup
{
    public string? Format { get; init; }
    public string? Width { get; init; }
    public string? Height { get; init; }
    public bool? Landscape { get; init; }
    public bool? PrintBackground { get; init; }
    public bool? PreferCssPageSize { get; init; }
    public PageMarginsSetup? Margins { get; init; }

    private bool SetsSize => Format is not null || Width is not null || Height is not null;

    public PdfRenderOptions ApplyTo(PdfRenderOptions options)
    {
        var resized = Format is not null
            ? options with { Format = Format, Width = null, Height = null }
            : Width is not null || Height is not null
                ? options with { Width = Width ?? options.Width, Height = Height ?? options.Height }
                : options;

        return resized with
        {
            Landscape = Landscape ?? options.Landscape,
            PrintBackground = PrintBackground ?? options.PrintBackground,
            PreferCssPageSize = PreferCssPageSize ?? (!SetsSize && options.PreferCssPageSize),
            Margins = Margins?.ApplyTo(options.Margins) ?? options.Margins
        };
    }
}

/// <summary>Per-side margin adjustments; a side left null keeps the template's margin.</summary>
public sealed record PageMarginsSetup(string? Top = null, string? Right = null, string? Bottom = null, string? Left = null)
{
    public PageMargins ApplyTo(PageMargins margins) => new(
        Top ?? margins.Top,
        Right ?? margins.Right,
        Bottom ?? margins.Bottom,
        Left ?? margins.Left);
}
