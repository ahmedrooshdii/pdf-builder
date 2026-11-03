using System.ComponentModel.DataAnnotations;

namespace PdfBuilder.Infrastructure.Jobs;

public sealed class JobStoreOptions
{
    public const string Section = "JobStore";

    /// <summary>
    /// How long a job stays pollable after its last update. Jobs live in the database, so this is
    /// shared by every instance and a restart does not shorten it.
    /// </summary>
    [Range(1, 168)]
    public int RetentionHours { get; init; } = 24;
}
