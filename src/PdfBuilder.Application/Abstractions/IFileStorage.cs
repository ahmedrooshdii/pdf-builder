namespace PdfBuilder.Application.Abstractions;

/// <summary>A stored artefact. Links to it are made from <paramref name="FileId"/> when a caller asks; none is stored.</summary>
/// <param name="CreatedAt">When it was written; retention is counted from here.</param>
public sealed record StoredFile(string FileId, long SizeBytes, DateTimeOffset CreatedAt);

/// <summary>
/// Where rendered PDFs live. Local disk today; an object-store implementation is a drop-in
/// replacement, which is what makes the Claim Check response work unchanged when scaling out.
/// </summary>
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(byte[] content, string contentType, CancellationToken cancellationToken = default);

    /// <returns>A readable stream, or <c>null</c> when the file does not exist. The caller disposes it.</returns>
    Task<Stream?> OpenReadAsync(string fileId, CancellationToken cancellationToken = default);

    /// <returns><c>false</c> when the file does not exist, and for an id that is not one the storage issues.</returns>
    Task<bool> ExistsAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>Deletes every file created before <paramref name="cutoff"/>. Idempotent; skips files it cannot delete now.</summary>
    /// <returns>How many files were deleted.</returns>
    Task<int> DeleteCreatedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
