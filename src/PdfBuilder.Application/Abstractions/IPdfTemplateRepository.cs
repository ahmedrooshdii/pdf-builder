using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Application.Abstractions;

/// <summary>Where templates are looked up. Implemented over the file system today.</summary>
public interface IPdfTemplateRepository
{
    /// <returns>The template, or <c>null</c> when no template is registered under <paramref name="templateId"/>.</returns>
    Task<PdfTemplate?> GetByIdAsync(string templateId, CancellationToken cancellationToken = default);
}
