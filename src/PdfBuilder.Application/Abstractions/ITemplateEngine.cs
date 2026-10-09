namespace PdfBuilder.Application.Abstractions;

/// <summary>Binds a JSON payload to a template and produces final HTML.</summary>
public interface ITemplateEngine
{
    /// <exception cref="Domain.Exceptions.TemplateRenderException">
    /// The template has a syntax error, failed at runtime, or the payload is not valid JSON.
    /// </exception>
    Task<string> RenderAsync(string templateHtml, string jsonPayload, CancellationToken cancellationToken = default);
}
