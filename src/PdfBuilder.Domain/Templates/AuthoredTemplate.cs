using PdfBuilder.Domain.Exceptions;

namespace PdfBuilder.Domain.Templates;

public enum TemplateStatus
{
    /// <summary>Never published: jobs cannot use it yet.</summary>
    Draft = 0,

    /// <summary>Published, and the draft has not changed since.</summary>
    Published = 1,

    /// <summary>Published, with draft edits that jobs do not see until the next publish.</summary>
    Modified = 2
}

/// <summary>
/// A template written through the API. It always has a draft, which is what people edit, and gains
/// a published version the first time it is published. Jobs only ever render the published one, so
/// editing a live template cannot break the documents being generated from it.
/// </summary>
/// <remarks>
/// <see cref="Revision"/> goes up on every change and is the optimistic-concurrency token: a save
/// says which revision it was based on, and a stale one is refused rather than silently
/// overwriting someone else's edit.
/// </remarks>
public sealed record AuthoredTemplate
{
    private AuthoredTemplate() { }

    public required string Key { get; init; }
    public required TemplateContent Draft { get; init; }
    public TemplateContent? Published { get; init; }
    public required int Revision { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }

    public TemplateStatus Status => Published is null
        ? TemplateStatus.Draft
        : Published == Draft ? TemplateStatus.Published : TemplateStatus.Modified;

    public static AuthoredTemplate Create(string key, TemplateContent draft, DateTimeOffset now)
    {
        if (!TemplateKey.IsValid(key))
            throw new ArgumentException($"'{key}' is not a valid template key.", nameof(key));

        return new AuthoredTemplate { Key = key, Draft = draft, Revision = 1, CreatedAt = now, UpdatedAt = now };
    }

    /// <summary>Rebuilds a template from storage. Not for creating new ones: use <see cref="Create"/>.</summary>
    public static AuthoredTemplate Restore(
        string key,
        TemplateContent draft,
        TemplateContent? published,
        int revision,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? publishedAt) => new()
    {
        Key = key,
        Draft = draft,
        Published = published,
        Revision = revision,
        CreatedAt = createdAt,
        UpdatedAt = updatedAt,
        PublishedAt = publishedAt
    };

    /// <exception cref="StaleTemplateRevisionException">Someone else saved since <paramref name="basedOnRevision"/>.</exception>
    public AuthoredTemplate SaveDraft(TemplateContent draft, int basedOnRevision, DateTimeOffset now)
    {
        EnsureCurrent(basedOnRevision);
        return draft == Draft ? this : this with { Draft = draft, Revision = Revision + 1, UpdatedAt = now };
    }

    /// <summary>Makes the current draft what jobs render. Publishing an unchanged draft changes nothing.</summary>
    /// <exception cref="StaleTemplateRevisionException">The draft changed since <paramref name="basedOnRevision"/>.</exception>
    public AuthoredTemplate Publish(int basedOnRevision, DateTimeOffset now)
    {
        EnsureCurrent(basedOnRevision);
        return Status == TemplateStatus.Published
            ? this
            : this with { Published = Draft, Revision = Revision + 1, UpdatedAt = now, PublishedAt = now };
    }

    private void EnsureCurrent(int basedOnRevision)
    {
        if (basedOnRevision != Revision)
            throw new StaleTemplateRevisionException(Key, basedOnRevision, Revision);
    }
}
