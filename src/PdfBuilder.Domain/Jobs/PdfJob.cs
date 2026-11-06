namespace PdfBuilder.Domain.Jobs;

/// <summary>
/// The trackable state of one generation request.
/// </summary>
/// <remarks>
/// Rendering happens off the request thread, so the HTTP caller gets a job id instead of a PDF.
/// This record is what <c>GET /api/pdf/jobs/{id}</c> reports back while the caller polls. Callers
/// that speak the message bus do not need it: they receive <see cref="Contracts.IGeneratePdfResponse"/>.
/// </remarks>
public sealed record PdfJob
{
    private PdfJob() { }

    public required Guid JobId { get; init; }
    public required string TemplateKey { get; init; }
    public required PdfJobStatus Status { get; init; }
    public required DateTimeOffset EnqueuedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
    public string? FileId { get; init; }
    public long? SizeBytes { get; init; }

    /// <summary>When the PDF was written. Retention is counted from here. Null unless the job succeeded.</summary>
    public DateTimeOffset? FileCreatedAt { get; init; }

    /// <summary>
    /// The file was deleted after its retention period, or is gone from storage. Links are made per read
    /// from <see cref="FileId"/>; none is stored.
    /// </summary>
    public bool FileExpired { get; init; }

    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static PdfJob Enqueued(Guid jobId, string templateKey, DateTimeOffset enqueuedAt) => new()
    {
        JobId = jobId,
        TemplateKey = templateKey,
        Status = PdfJobStatus.Pending,
        EnqueuedAt = enqueuedAt
    };

    public PdfJob Succeed(string fileId, long sizeBytes, DateTimeOffset fileCreatedAt, DateTimeOffset completedAt) => this with
    {
        Status = PdfJobStatus.Succeeded,
        FileId = fileId,
        SizeBytes = sizeBytes,
        FileCreatedAt = fileCreatedAt,
        FileExpired = false,
        CompletedAt = completedAt,
        ErrorCode = null,
        ErrorMessage = null
    };

    public PdfJob Fail(string errorCode, string errorMessage, DateTimeOffset completedAt) => this with
    {
        Status = PdfJobStatus.Failed,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage,
        CompletedAt = completedAt,
        FileId = null,
        FileCreatedAt = null,
        FileExpired = false,
        SizeBytes = null
    };

    /// <summary>The same job, reporting that its file is gone. Only a succeeded job has a file to lose.</summary>
    public PdfJob WithFileExpired() => Status == PdfJobStatus.Succeeded
        ? this with { FileExpired = true }
        : throw new InvalidOperationException("Only a succeeded job has a file that can expire.");
}
