namespace PdfBuilder.Domain.Exceptions;

/// <summary>
/// Base class for every failure this service reports to a caller.
/// </summary>
/// <remarks>
/// <see cref="IsTransient"/> is the single switch that drives the messaging retry policy, so the
/// decision lives on the exception that knows the answer rather than in a <c>catch</c> filter that
/// has to guess. Transient failures bubble out of the consumer and are redelivered; permanent ones
/// are answered immediately with a negative response.
/// </remarks>
public abstract class PdfBuilderException(string errorCode, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>One of <see cref="Contracts.PdfErrorCodes"/>.</summary>
    public string ErrorCode { get; } = errorCode;

    /// <summary>True when redelivering the same message could succeed.</summary>
    public abstract bool IsTransient { get; }
}
