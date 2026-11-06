using PdfBuilder.Domain.Jobs;

namespace PdfBuilder.Infrastructure.Persistence.Records;

/// <summary>
/// The stored shape of a <see cref="PdfJob"/>. The domain record has a private constructor and is
/// never mapped directly, so the table can change without the domain noticing and the other way round.
/// </summary>
internal sealed class PdfJobRecord
{
    public Guid JobId { get; set; }

    /// <summary>The client that owns this job; never changes after creation (FR-001).</summary>
    public string ClientId { get; set; } = string.Empty;

    public string TemplateKey { get; set; } = string.Empty;

    /// <summary>The <see cref="PdfJobStatus"/> name, as the API returns it.</summary>
    public string Status { get; set; } = string.Empty;

    public DateTimeOffset EnqueuedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FileId { get; set; }
    /// <summary>When the PDF was written; retention is counted from here. Set for every succeeded job.</summary>
    public DateTimeOffset? FileCreatedAt { get; set; }

    /// <summary>When the file was found deleted or missing. Never cleared once set.</summary>
    public DateTimeOffset? FileExpiredAt { get; set; }

    public long? SizeBytes { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// When this row was last written. Retention counts from here, which is what the in-memory store
    /// did: every save gave the job a fresh expiry.
    /// </summary>
    public DateTimeOffset LastSavedAt { get; set; }

    public static PdfJobRecord From(PdfJob job, string clientId, DateTimeOffset savedAt) => new()
    {
        JobId = job.JobId,
        ClientId = clientId,
        TemplateKey = job.TemplateKey,
        Status = job.Status.ToString(),
        EnqueuedAt = job.EnqueuedAt.ToUniversalTime(),
        CompletedAt = job.CompletedAt?.ToUniversalTime(),
        FileId = job.FileId,
        FileCreatedAt = job.FileCreatedAt?.ToUniversalTime(),
        // A job that reports its file expired has no timestamp of its own: it was marked in the store.
        FileExpiredAt = job.FileExpired ? savedAt.ToUniversalTime() : null,
        SizeBytes = job.SizeBytes,
        ErrorCode = job.ErrorCode,
        ErrorMessage = job.ErrorMessage,
        LastSavedAt = savedAt.ToUniversalTime()
    };

    /// <summary>
    /// Overwrites every stored value except the key with those of <paramref name="other"/>. The owner is
    /// deliberately not copied: a job never changes hands (FR-001).
    /// </summary>
    public void CopyFrom(PdfJobRecord other)
    {
        TemplateKey = other.TemplateKey;
        Status = other.Status;
        EnqueuedAt = other.EnqueuedAt;
        CompletedAt = other.CompletedAt;
        FileId = other.FileId;
        FileCreatedAt = other.FileCreatedAt;
        // A file never comes back: a mark that is set stays, whatever a later save says.
        FileExpiredAt ??= other.FileExpiredAt;
        SizeBytes = other.SizeBytes;
        ErrorCode = other.ErrorCode;
        ErrorMessage = other.ErrorMessage;
        LastSavedAt = other.LastSavedAt;
    }

    /// <summary>Rebuilds the job through its own factory and transitions, so its invariants hold.</summary>
    public PdfJob ToDomain()
    {
        var pending = PdfJob.Enqueued(JobId, TemplateKey, EnqueuedAt);

        return Enum.Parse<PdfJobStatus>(Status) switch
        {
            PdfJobStatus.Succeeded => Expire(pending.Succeed(
                FileId ?? string.Empty, SizeBytes ?? 0, FileCreatedAt ?? CompletedAt ?? EnqueuedAt, CompletedAt ?? EnqueuedAt)),
            PdfJobStatus.Failed => pending.Fail(
                ErrorCode ?? string.Empty, ErrorMessage ?? string.Empty, CompletedAt ?? EnqueuedAt),
            _ => pending
        };
    }

    private PdfJob Expire(PdfJob succeeded) => FileExpiredAt is null ? succeeded : succeeded.WithFileExpired();
}
