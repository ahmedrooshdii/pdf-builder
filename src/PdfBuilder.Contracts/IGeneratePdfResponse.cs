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

    /// <summary>
    /// Absolute URL the requester downloads the PDF from with a plain GET, no credentials. When the
    /// deployment requires signed links, it is signed and stops working at <see cref="FileUrlExpiresAt"/>;
    /// ask <c>GET /api/pdf/jobs/{CorrelationId}/file-url</c> for a fresh one. Otherwise it is not signed and
    /// works until the file is deleted after the deployment's retention period. Null when
    /// <see cref="IsSuccess"/> is false.
    /// </summary>
    string? FileUrl { get; }

    /// <summary>
    /// When <see cref="FileUrl"/> stops working. Null when the deployment does not sign links, and when
    /// <see cref="IsSuccess"/> is false. New in 1.3.0: a 1.2.0 consumer ignores it.
    /// </summary>
    DateTimeOffset? FileUrlExpiresAt { get; }
    /// <summary>Size of the stored PDF in bytes. Null when <see cref="IsSuccess"/> is false.</summary>
    long? SizeBytes { get; }

    /// <summary>One of <see cref="PdfErrorCodes"/>. Null when <see cref="IsSuccess"/> is true.</summary>
    string? ErrorCode { get; }

    /// <summary>Human-readable detail for logs and support. Not for branching: use <see cref="ErrorCode"/>.</summary>
    string? ErrorMessage { get; }

    /// <summary>When the job reached its final state.</summary>
    DateTimeOffset CompletedAt { get; }
}
