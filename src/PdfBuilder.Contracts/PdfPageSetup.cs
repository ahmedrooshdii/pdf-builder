namespace PdfBuilder.Contracts;

/// <summary>
/// Page setup for one request, on top of the template's own. Every member is optional: set only
/// what this document needs to differ in, and the template supplies the rest.
/// </summary>
/// <remarks>
/// Size is either <see cref="Format"/> or <see cref="Width"/> with <see cref="Height"/>, never
/// both. Setting a size also stops a CSS <c>@page { size: … }</c> rule in the template from
/// overriding it, unless <see cref="PreferCssPageSize"/> is set to true.
/// </remarks>
public sealed class PdfPageSetup
{
    /// <summary>A named paper size: <c>A0</c>–<c>A6</c>, <c>Letter</c>, <c>Legal</c>, <c>Tabloid</c> or <c>Ledger</c>.</summary>
    public string? Format { get; set; }

    /// <summary>Custom page width as a CSS length, for example <c>80mm</c> for a till receipt. Requires <see cref="Height"/>.</summary>
    public string? Width { get; set; }

    /// <summary>
    /// Custom page height as a CSS length (<c>mm</c>, <c>cm</c>, <c>in</c> or <c>px</c>), or <c>auto</c> for one page
    /// exactly as tall as the content, the way a till prints a receipt off a roll. Requires <see cref="Width"/>.
    /// </summary>
    public string? Height { get; set; }

    /// <summary>True prints the page turned on its side.</summary>
    public bool? Landscape { get; set; }

    /// <summary>When false, background colours and images are dropped from the output.</summary>
    public bool? PrintBackground { get; set; }

    /// <summary>When true, a CSS <c>@page { size: … }</c> rule in the template wins over the size given here.</summary>
    public bool? PreferCssPageSize { get; set; }

    /// <summary>Margins to change. A side left null keeps the template's margin.</summary>
    public PdfPageMargins? Margins { get; set; }
}

/// <summary>Page margins as CSS lengths (<c>10mm</c>, <c>0.5in</c>). A null side keeps the template's margin.</summary>
public sealed class PdfPageMargins
{
    /// <summary>Top margin.</summary>
    public string? Top { get; set; }
    /// <summary>Right margin.</summary>
    public string? Right { get; set; }
    /// <summary>Bottom margin.</summary>
    public string? Bottom { get; set; }
    /// <summary>Left margin.</summary>
    public string? Left { get; set; }
}
