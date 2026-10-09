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
    public required string TemplateId { get; init; }
    public required PdfJobStatus Status { get; init; }
    public required DateTimeOffset EnqueuedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
    public string? FileId { get; init; }
    public string? FileUrl { get; init; }
    public long? SizeBytes { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static PdfJob Enqueued(Guid jobId, string templateId, DateTimeOffset enqueuedAt) => new()
    {
        JobId = jobId,
        TemplateId = templateId,
        Status = PdfJobStatus.Pending,
        EnqueuedAt = enqueuedAt
    };

    public PdfJob Succeed(string fileId, string fileUrl, long sizeBytes, DateTimeOffset completedAt) => this with
    {
        Status = PdfJobStatus.Succeeded,
        FileId = fileId,
        FileUrl = fileUrl,
        SizeBytes = sizeBytes,
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
        FileUrl = null,
        SizeBytes = null
    };
}
