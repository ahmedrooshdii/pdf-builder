using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Infrastructure.Persistence.Records;

/// <summary>The stored shape of an <see cref="AuthoredTemplate"/>. Its status is derived, so it is not a column.</summary>
internal sealed class AuthoredTemplateRecord
{
    /// <summary>The client that owns this template; never changes after creation (FR-001).</summary>
    public string ClientId { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;
    public TemplateContentRecord Draft { get; set; } = null!;

    /// <summary>Null until the template is first published.</summary>
    public TemplateContentRecord? Published { get; set; }

    /// <summary>The optimistic-concurrency token: a save says which revision it was based on.</summary>
    public int Revision { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    public static AuthoredTemplateRecord From(AuthoredTemplate template, string clientId) => new()
    {
        ClientId = clientId,
        Key = template.Key,
        Draft = TemplateContentRecord.From(template.Draft),
        Published = template.Published is null ? null : TemplateContentRecord.From(template.Published),
        Revision = template.Revision,
        CreatedAt = template.CreatedAt.ToUniversalTime(),
        UpdatedAt = template.UpdatedAt.ToUniversalTime(),
        PublishedAt = template.PublishedAt?.ToUniversalTime()
    };

    public AuthoredTemplate ToDomain() => AuthoredTemplate.Restore(
        Key, Draft.ToDomain(), Published?.ToDomain(), Revision, CreatedAt, UpdatedAt, PublishedAt);
}
