using PdfBuilder.Contracts;

namespace PdfBuilder.Domain.Exceptions;

/// <summary>
/// The browser failed to turn valid HTML into a PDF: a crash, a timeout, or a lost connection.
/// Transient by definition, so it is retried rather than reported straight back to the caller.
/// </summary>
public sealed class PdfRenderException(string message, Exception? innerException = null)
    : PdfBuilderException(PdfErrorCodes.RenderFailed, message, innerException)
{
    public override bool IsTransient => true;
}
