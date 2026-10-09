using PdfBuilder.Contracts;

namespace PdfBuilder.Domain.Exceptions;

/// <summary>No template is registered under <see cref="TemplateId"/>.</summary>
public sealed class TemplateNotFoundException(string templateId)
    : DomainException(PdfErrorCodes.TemplateNotFound, $"Template '{templateId}' does not exist.")
{
    public string TemplateId { get; } = templateId;
}
