using System.ComponentModel.DataAnnotations;

namespace PdfBuilder.Infrastructure.Jobs;

public sealed class JobStoreOptions
{
    public const string Section = "JobStore";

    /// <summary>How long a finished job stays pollable before it is forgotten.</summary>
    [Range(1, 168)]
    public int RetentionHours { get; init; } = 24;
}
