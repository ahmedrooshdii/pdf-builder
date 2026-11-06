using System.Linq.Expressions;
using PdfBuilder.Domain.Jobs;
using PdfBuilder.Infrastructure.Persistence.Records;

namespace PdfBuilder.Infrastructure.Jobs;

/// <summary>
/// Which job rows a caller can still read, in one place so reads and the sweeper agree (feature 005, FR-019).
/// </summary>
/// <remarks>
/// A job is readable for the job retention period after its last save. A succeeded job is also readable
/// for as long as its file is kept, and for one more job retention period after the file expired, so a
/// poller can still be told that the file is gone.
/// </remarks>
internal static class JobVisibility
{
    public static Expression<Func<PdfJobRecord, bool>> IsReadable(DateTimeOffset now, int retentionHours, int fileRetentionDays)
    {
        var job = TimeSpan.FromHours(retentionHours);
        var savedCutoff = now - job;
        var createdCutoff = now - TimeSpan.FromDays(fileRetentionDays) - job;
        var expiredCutoff = now - job;
        var succeeded = nameof(PdfJobStatus.Succeeded);

        return j => j.LastSavedAt >= savedCutoff
            || (j.Status == succeeded
                && j.FileCreatedAt >= createdCutoff
                && (j.FileExpiredAt == null || j.FileExpiredAt >= expiredCutoff));
    }
}
