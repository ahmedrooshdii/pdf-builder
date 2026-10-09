namespace PdfBuilder.Domain.Jobs;

public enum PdfJobStatus
{
    /// <summary>Accepted and queued; no worker has finished it yet.</summary>
    Pending = 0,

    /// <summary>A PDF was produced and stored.</summary>
    Succeeded = 1,

    /// <summary>The job will not produce a PDF. <see cref="PdfJob.ErrorCode"/> says why.</summary>
    Failed = 2
}
