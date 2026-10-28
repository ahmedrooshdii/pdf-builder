namespace PdfBuilder.Domain.Exceptions;

/// <summary>
/// Codes for template-management failures. HTTP-only: unlike <see cref="Contracts.PdfErrorCodes"/>
/// they never travel on the message bus, so they are not part of the published contract.
/// </summary>
public static class TemplateErrorCodes
{
    public const string KeyTaken = "TEMPLATE_KEY_TAKEN";
    public const string ReadOnly = "TEMPLATE_READ_ONLY";
    public const string RevisionConflict = "TEMPLATE_REVISION_CONFLICT";
}

/// <summary>A template already exists under this key, built in or authored. Keys are unique ignoring case.</summary>
public sealed class TemplateKeyTakenException(string templateKey)
    : DomainException(TemplateErrorCodes.KeyTaken, $"A template with the key '{templateKey}' already exists.")
{
    public string TemplateKey { get; } = templateKey;
}

/// <summary>The template ships with the service (the templates/ folder) and is changed through git, not the API.</summary>
public sealed class TemplateReadOnlyException(string templateKey)
    : DomainException(
        TemplateErrorCodes.ReadOnly,
        $"Template '{templateKey}' is built in and cannot be changed here. Duplicate it to make an editable copy.")
{
    public string TemplateKey { get; } = templateKey;
}

/// <summary>The template changed since the caller last read it; their edit was not applied.</summary>
public sealed class StaleTemplateRevisionException(string templateKey, int basedOnRevision, int currentRevision)
    : DomainException(
        TemplateErrorCodes.RevisionConflict,
        $"Template '{templateKey}' is at revision {currentRevision}, but the change was based on revision {basedOnRevision}. Reload it and try again.")
{
    public string TemplateKey { get; } = templateKey;
    public int CurrentRevision { get; } = currentRevision;
}
