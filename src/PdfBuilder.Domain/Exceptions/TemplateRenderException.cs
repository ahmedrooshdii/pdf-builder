using PdfBuilder.Contracts;

namespace PdfBuilder.Domain.Exceptions;

/// <summary>The template could not be bound to the payload: a syntax error, or a runtime error while rendering.</summary>
public sealed class TemplateRenderException(string message, Exception? innerException = null)
    : DomainException(PdfErrorCodes.TemplateRenderFailed, message, innerException);
