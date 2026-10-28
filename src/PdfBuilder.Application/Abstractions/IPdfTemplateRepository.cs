using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Application.Abstractions;

/// <summary>What the render pipeline asks for: the template a job should print, by key.</summary>
public interface IPdfTemplateRepository
{
    /// <returns>
    /// The printable template, or <c>null</c> when nothing usable is registered under
    /// <paramref name="templateKey"/>, including when an authored template exists but has never been published.
    /// </returns>
    Task<PdfTemplate?> GetByKeyAsync(string templateKey, CancellationToken cancellationToken = default);
}
