using MediatR;

namespace PdfBuilder.Application.Features.Templates;

public sealed record ListTemplatesQuery : IRequest<IReadOnlyList<TemplateSummary>>;

public sealed class ListTemplatesQueryHandler(TemplateCatalog catalog)
    : IRequestHandler<ListTemplatesQuery, IReadOnlyList<TemplateSummary>>
{
    public async Task<IReadOnlyList<TemplateSummary>> Handle(ListTemplatesQuery request, CancellationToken cancellationToken)
    {
        var templates = await catalog.ListAsync(cancellationToken);
        return [.. templates.Select(t => t.ToSummary()).OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase)];
    }
}

/// <summary>One template with its draft and published content.</summary>
/// <exception cref="Domain.Exceptions.TemplateNotFoundException">No template has this key.</exception>
public sealed record GetTemplateQuery(string TemplateKey) : IRequest<TemplateView>;

public sealed class GetTemplateQueryHandler(TemplateCatalog catalog) : IRequestHandler<GetTemplateQuery, TemplateView>
{
    public Task<TemplateView> Handle(GetTemplateQuery request, CancellationToken cancellationToken) =>
        catalog.GetAsync(request.TemplateKey, cancellationToken);
}
