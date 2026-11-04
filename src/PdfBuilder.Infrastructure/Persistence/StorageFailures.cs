using Blocks.ServiceDefaults.EntityFramework;
using Blocks.ServiceDefaults.Problems;

namespace PdfBuilder.Infrastructure.Persistence;

/// <summary>Tells "the database cannot be reached right now" apart from every other failure.</summary>
/// <remarks>
/// The rule lives in the shared package, so the jobs and templates here and the API key checks there
/// can never disagree about what an outage is.
/// </remarks>
public static class StorageFailures
{
    /// <summary>The <c>code</c> a caller receives, in a 503 problem, when storage is unavailable.</summary>
    public const string UnavailableCode = BlocksProblemCodes.StorageUnavailable;

    /// <inheritdoc cref="NpgsqlStorageFailures.IsUnavailable"/>
    public static bool IsUnavailable(Exception exception) => NpgsqlStorageFailures.IsUnavailable(exception);
}
