namespace PdfBuilder.Domain.Exceptions;

/// <summary>A permanent business failure: the request itself is wrong, so retrying cannot help.</summary>
public abstract class DomainException(string errorCode, string message, Exception? innerException = null)
    : PdfBuilderException(errorCode, message, innerException)
{
    public sealed override bool IsTransient => false;
}
