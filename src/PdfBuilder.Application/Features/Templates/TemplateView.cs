using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Application.Features.Templates;

public enum TemplateSource
{
    /// <summary>Written through the API; editable.</summary>
    Authored = 0,

    /// <summary>Shipped in the templates/ folder; read-only.</summary>
    BuiltIn = 1
}

/// <summary>One template as callers see it, whichever store it lives in.</summary>
public sealed record TemplateView(
    string Key,
    TemplateSource Source,
    TemplateStatus Status,
    int Revision,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    TemplateContent Draft,
    TemplateContent? Published)
{
    public static TemplateView From(AuthoredTemplate template) => new(
        template.Key,
        TemplateSource.Authored,
        template.Status,
        template.Revision,
        template.UpdatedAt,
        template.PublishedAt,
        template.Draft,
        template.Published);

    /// <summary>A built-in template is its own draft and its own published version, at revision 0.</summary>
    public static TemplateView From(BuiltInTemplate template) => new(
        template.Key,
        TemplateSource.BuiltIn,
        TemplateStatus.Published,
        Revision: 0,
        template.UpdatedAt,
        template.UpdatedAt,
        template.Content,
        template.Content);

    public TemplateSummary ToSummary() => new(Key, Source, Status, Revision, UpdatedAt, PublishedAt);
}

/// <summary>A template without its content, for listings.</summary>
public sealed record TemplateSummary(
    string Key,
    TemplateSource Source,
    TemplateStatus Status,
    int Revision,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt);
