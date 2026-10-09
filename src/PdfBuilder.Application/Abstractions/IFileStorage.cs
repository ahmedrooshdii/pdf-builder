namespace PdfBuilder.Application.Abstractions;

/// <summary>A stored artefact and the URL a caller can fetch it from.</summary>
public sealed record StoredFile(string FileId, string Url, long SizeBytes);

/// <summary>
/// Where rendered PDFs live. Local disk today; an object-store implementation is a drop-in
/// replacement, which is what makes the Claim Check response work unchanged when scaling out.
/// </summary>
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(byte[] content, string contentType, CancellationToken cancellationToken = default);

    /// <returns>A readable stream, or <c>null</c> when the file does not exist. The caller disposes it.</returns>
    Task<Stream?> OpenReadAsync(string fileId, CancellationToken cancellationToken = default);
}
