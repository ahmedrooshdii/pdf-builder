namespace PdfBuilder.Infrastructure.Templates;

public sealed class TemplateEngineOptions
{
    public const string Section = "TemplateEngine";

    /// <summary>
    /// How the engine treats a field the payload does not contain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off (the default): a missing field renders as nothing, the way every mainstream HTML
    /// templating engine behaves. Optional sections stay readable, and one absent field cannot
    /// take down a document that is otherwise complete.
    /// </para>
    /// <para>
    /// On: any reference to a field the payload does not contain fails the job with the field name
    /// and its position in the template. Worth enabling in staging and in template tests, where a
    /// typo should be loud; it is the only way to tell a deliberate blank from a misspelling.
    /// </para>
    /// </remarks>
    public bool StrictMode { get; init; }
}
