namespace PdfBuilder.Domain.Templates;

/// <summary>
/// Everything a person writes to make a template: the markup, its stylesheet, an example payload,
/// and the page setup. Value semantics, so "the draft differs from what is published" is a plain
/// equality check.
/// </summary>
/// <param name="Html">Scriban-flavoured HTML.</param>
/// <param name="Css">Stylesheet injected into the document's head when it is printed. May be empty.</param>
/// <param name="SampleJson">An example payload: used for previews, the publish check and "Load sample".</param>
/// <param name="Options">Paper size, orientation and margins.</param>
public sealed record TemplateContent(string Html, string Css, string SampleJson, PdfRenderOptions Options)
{
    public const string EmptySample = "{}";

    /// <summary>
    /// The single HTML document the engine and the browser see: <see cref="Css"/> goes in a
    /// <c>&lt;style&gt;</c> element just before <c>&lt;/head&gt;</c>, or at the very top when the
    /// markup has no head. It passes through the template engine too, so it may use <c>{{ }}</c>.
    /// </summary>
    public string ComposeHtml()
    {
        if (string.IsNullOrWhiteSpace(Css))
            return Html;

        var style = $"<style>\n{Css}\n</style>\n";
        var headEnd = Html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);

        return headEnd < 0 ? style + Html : Html.Insert(headEnd, style);
    }

    /// <summary>The payload to bind when none is given: the sample, or an empty object if there is none.</summary>
    public string SampleOrEmpty => string.IsNullOrWhiteSpace(SampleJson) ? EmptySample : SampleJson;

    public PdfTemplate ToPdfTemplate(string key) => new(key, ComposeHtml(), Options);
}
