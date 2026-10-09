namespace PdfBuilder.Contracts;

/// <summary>
/// The outcome of a <see cref="IGeneratePdfRequest"/>. Follows the Claim Check pattern: the message
/// carries a reference to the stored PDF, never the bytes, so the broker stays free of large payloads.
/// </summary>
public interface IGeneratePdfResponse
{
    /// <summary>Echoes <see cref="IGeneratePdfRequest.CorrelationId"/>.</summary>
    Guid CorrelationId { get; }

    /// <summary>True when a PDF was produced and stored.</summary>
    bool IsSuccess { get; }

    /// <summary>Opaque id of the stored file. Null when <see cref="IsSuccess"/> is false.</summary>
    string? FileId { get; }

    /// <summary>Absolute URL the requester downloads the PDF from. Null when <see cref="IsSuccess"/> is false.</summary>
    string? FileUrl { get; }

    /// <summary>Size of the stored PDF in bytes. Null when <see cref="IsSuccess"/> is false.</summary>
    long? SizeBytes { get; }

    /// <summary>One of <see cref="PdfErrorCodes"/>. Null when <see cref="IsSuccess"/> is true.</summary>
    string? ErrorCode { get; }

    /// <summary>Human-readable detail for logs and support. Not for branching: use <see cref="ErrorCode"/>.</summary>
    string? ErrorMessage { get; }

    /// <summary>When the job reached its final state.</summary>
    DateTimeOffset CompletedAt { get; }
}
