using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PdfBuilder.Application.Abstractions;

namespace PdfBuilder.Infrastructure.Storage;

/// <summary>
/// Stores PDFs on local disk.
/// </summary>
/// <remarks>
/// Fine for a single instance with a mounted volume. Replace with an object-store implementation of
/// <see cref="IFileStorage"/> before running more than one replica: with local disk, a download
/// request load-balanced to a different instance than the one that rendered the file gets a 404.
/// A file's last-write time is its creation time, which is what retention counts from.
/// </remarks>
public sealed partial class LocalFileStorage(IOptions<StorageOptions> options, TimeProvider clock) : IFileStorage
{
    /// <summary>An abandoned temporary file older than this is a failed write and is removed by the sweep.</summary>
    private static readonly TimeSpan StaleTemporaryFileAge = TimeSpan.FromHours(1);

    private readonly string _root = Path.GetFullPath(options.Value.RootPath, AppContext.BaseDirectory);

    /// <summary>The directory files are written to, resolved to an absolute path.</summary>
    public string RootPath => _root;

    public async Task<StoredFile> SaveAsync(byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        var fileId = Guid.NewGuid().ToString("N");
        var path = ResolvePath(fileId);
        var createdAt = clock.GetUtcNow();

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a temporary name and move into place, so a reader never observes a half-written PDF.
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, path);
            // The file's age is what retention is counted from; the job records the same instant.
            File.SetLastWriteTimeUtc(path, createdAt.UtcDateTime);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }

        return new StoredFile(fileId, content.LongLength, createdAt);
    }

    public Task<Stream?> OpenReadAsync(string fileId, CancellationToken cancellationToken = default)
    {
        // The id reaches us straight from a URL segment, so reject anything that is not one we issued.
        if (!IsIssuedId(fileId))
            return Task.FromResult<Stream?>(null);

        try
        {
            // FileShare.Delete: a sweep may delete the file while a download is reading it; on Unix the
            // reader then finishes from the unlinked file.
            Stream stream = new FileStream(
                ResolvePath(fileId),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return Task.FromResult<Stream?>(stream);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task<bool> ExistsAsync(string fileId, CancellationToken cancellationToken = default) =>
        Task.FromResult(IsIssuedId(fileId) && File.Exists(ResolvePath(fileId)));

    public Task<int> DeleteCreatedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        var deleted = 0;
        if (!Directory.Exists(_root))
            return Task.FromResult(deleted);

        var staleTemporary = clock.GetUtcNow() - StaleTemporaryFileAge;
        foreach (var shard in Directory.EnumerateDirectories(_root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ShardPattern().IsMatch(Path.GetFileName(shard)))
                continue;

            foreach (var path in SafeEnumerate(shard))
            {
                var extension = Path.GetExtension(path);
                var limit = extension switch
                {
                    ".pdf" => cutoff,
                    ".tmp" => staleTemporary,
                    _ => (DateTimeOffset?)null
                };
                if (limit is null)
                    continue;

                try
                {
                    if (File.GetLastWriteTimeUtc(path) >= limit.Value.UtcDateTime)
                        continue;

                    File.Delete(path);
                    if (extension == ".pdf")
                        deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Gone already, or in use right now: the next sweep tries again.
                }
            }
        }

        return Task.FromResult(deleted);
    }

    private static List<string> SafeEnumerate(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsIssuedId(string? fileId) => !string.IsNullOrEmpty(fileId) && FileIdPattern().IsMatch(fileId);

    /// <summary>Shards on the first two hex characters, so no single directory collects every file.</summary>
    private string ResolvePath(string fileId) => Path.Combine(_root, fileId[..2], $"{fileId}.pdf");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: a stray .tmp file is harmless next to a failed write.
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex FileIdPattern();

    [GeneratedRegex("^[0-9a-f]{2}$")]
    private static partial Regex ShardPattern();
}
