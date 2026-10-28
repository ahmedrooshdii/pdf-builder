using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Application.Abstractions;

/// <summary>Templates written through the API. Writable, and must survive deployments.</summary>
public interface IAuthoredTemplateStore
{
    Task<IReadOnlyList<AuthoredTemplate>> ListAsync(CancellationToken cancellationToken = default);

    /// <returns>The template, or <c>null</c> when none is stored under <paramref name="templateKey"/>.</returns>
    Task<AuthoredTemplate?> GetAsync(string templateKey, CancellationToken cancellationToken = default);

    /// <exception cref="Domain.Exceptions.TemplateKeyTakenException">The key is already stored.</exception>
    Task AddAsync(AuthoredTemplate authoredTemplate, CancellationToken cancellationToken = default);

    /// <summary>Replaces the stored template, but only if it is still at <paramref name="expectedRevision"/>.</summary>
    /// <exception cref="Domain.Exceptions.StaleTemplateRevisionException">Someone else saved in between.</exception>
    /// <exception cref="Domain.Exceptions.TemplateNotFoundException">It was deleted in between.</exception>
    Task UpdateAsync(AuthoredTemplate authoredTemplate, int expectedRevision, CancellationToken cancellationToken = default);

    /// <returns><c>false</c> when there was nothing to delete.</returns>
    Task<bool> DeleteAsync(string templateKey, CancellationToken cancellationToken = default);
}

/// <summary>Templates that ship with the service. Read-only.</summary>
public interface IBuiltInTemplateSource
{
    Task<IReadOnlyList<BuiltInTemplate>> ListAsync(CancellationToken cancellationToken = default);

    /// <returns>The template, or <c>null</c> when none ships under <paramref name="templateKey"/>.</returns>
    Task<BuiltInTemplate?> GetByKeyAsync(string templateKey, CancellationToken cancellationToken = default);
}
