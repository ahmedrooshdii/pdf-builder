using PdfBuilder.Contracts;

namespace PdfBuilder.Domain.Exceptions;

/// <summary>No template is registered under <see cref="TemplateKey"/>.</summary>
public sealed class TemplateNotFoundException(string templateKey)
    : DomainException(PdfErrorCodes.TemplateNotFound, $"Template '{templateKey}' does not exist.")
{
    public string TemplateKey { get; } = templateKey;
}
