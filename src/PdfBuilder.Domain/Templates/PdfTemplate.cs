namespace PdfBuilder.Domain.Templates;

/// <summary>An HTML template plus the page setup it is meant to be printed with.</summary>
public sealed class PdfTemplate
{
    public PdfTemplate(string key, string html, PdfRenderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(html);

        Key = key;
        Html = html;
        Options = options ?? PdfRenderOptions.Default;
    }

    public string Key { get; }

    /// <summary>Template source, not yet bound to any data.</summary>
    public string Html { get; }

    public PdfRenderOptions Options { get; }
}
