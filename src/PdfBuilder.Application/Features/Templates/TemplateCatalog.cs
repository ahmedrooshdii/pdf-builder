using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Exceptions;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Application.Features.Templates;

/// <summary>
/// The two template stores seen as one. Keys are unique across both, ignoring case: a file system
/// that ignores case (Windows, macOS) would otherwise turn <c>Invoice</c> and <c>invoice</c> into
/// one file, and people reading a list would not tell them apart either.
/// </summary>
public sealed class TemplateCatalog(IAuthoredTemplateStore authored, IBuiltInTemplateSource builtIn)
{
    public async Task<IReadOnlyList<TemplateView>> ListAsync(CancellationToken cancellationToken)
    {
        var authoredTemplates = await authored.ListAsync(cancellationToken);
        var builtInTemplates = await builtIn.ListAsync(cancellationToken);

        return
        [
            .. authoredTemplates.Select(TemplateView.From),
            .. builtInTemplates
                .Where(b => !authoredTemplates.Any(a => SameKey(a.Key, b.Key)))
                .Select(TemplateView.From)
        ];
    }

    /// <exception cref="TemplateNotFoundException">Neither store has it.</exception>
    public async Task<TemplateView> GetAsync(string templateKey, CancellationToken cancellationToken)
    {
        if (await authored.GetAsync(templateKey, cancellationToken) is { } own)
            return TemplateView.From(own);

        return await builtIn.GetByKeyAsync(templateKey, cancellationToken) is { } shipped
            ? TemplateView.From(shipped)
            : throw new TemplateNotFoundException(templateKey);
    }

    /// <summary>The template to edit. Built-in ones are refused: they change through git.</summary>
    /// <exception cref="TemplateNotFoundException">Neither store has it.</exception>
    /// <exception cref="TemplateReadOnlyException">It is built in.</exception>
    public async Task<AuthoredTemplate> GetEditableAsync(string templateKey, CancellationToken cancellationToken)
    {
        if (await authored.GetAsync(templateKey, cancellationToken) is { } own)
            return own;

        throw await builtIn.GetByKeyAsync(templateKey, cancellationToken) is not null
            ? new TemplateReadOnlyException(templateKey)
            : new TemplateNotFoundException(templateKey);
    }

    /// <exception cref="TemplateKeyTakenException">Either store already has the key, in any letter case.</exception>
    public async Task EnsureKeyIsFreeAsync(string templateKey, CancellationToken cancellationToken)
    {
        var all = await ListAsync(cancellationToken);
        if (all.Any(t => SameKey(t.Key, templateKey)))
            throw new TemplateKeyTakenException(templateKey);
    }

    private static bool SameKey(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// What jobs render: an authored template's published version, or a built-in template. A draft
/// that was never published is invisible here, which is the whole point of having drafts.
/// </summary>
public sealed class PublishedTemplateRepository(IAuthoredTemplateStore authored, IBuiltInTemplateSource builtIn)
    : IPdfTemplateRepository
{
    public async Task<PdfTemplate?> GetByKeyAsync(string templateKey, CancellationToken cancellationToken = default)
    {
        if (await authored.GetAsync(templateKey, cancellationToken) is { Published: { } published })
            return published.ToPdfTemplate(templateKey);

        return (await builtIn.GetByKeyAsync(templateKey, cancellationToken))?.ToPdfTemplate();
    }
}
