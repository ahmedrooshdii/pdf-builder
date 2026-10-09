using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Application.Abstractions;

/// <summary>Turns final HTML into PDF bytes.</summary>
public interface IPdfRenderer
{
    /// <exception cref="Domain.Exceptions.PdfRenderException">
    /// The browser crashed, timed out, or disconnected. Transient: worth retrying.
    /// </exception>
    Task<byte[]> RenderAsync(string html, PdfRenderOptions renderOptions, CancellationToken cancellationToken = default);
}
