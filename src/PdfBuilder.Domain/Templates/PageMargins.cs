namespace PdfBuilder.Domain.Templates;

/// <summary>
/// Page margins as CSS lengths (<c>10mm</c>, <c>0.5in</c>, <c>72px</c>), the unit the browser expects.
/// </summary>
public sealed record PageMargins(
    string Top = PageMargins.DefaultLength,
    string Right = PageMargins.DefaultLength,
    string Bottom = PageMargins.DefaultLength,
    string Left = PageMargins.DefaultLength)
{
    private const string DefaultLength = "10mm";

    public static PageMargins Default { get; } = new();
}
