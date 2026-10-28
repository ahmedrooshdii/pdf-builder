namespace PdfBuilder.Domain.Templates;

/// <summary>
/// A template that ships with the service, from the repository's templates/ folder. Changed through
/// git and deployed by CI, so it is always published and never edited through the API.
/// </summary>
public sealed record BuiltInTemplate(string Key, TemplateContent Content, DateTimeOffset UpdatedAt)
{
    public PdfTemplate ToPdfTemplate() => Content.ToPdfTemplate(Key);
}
