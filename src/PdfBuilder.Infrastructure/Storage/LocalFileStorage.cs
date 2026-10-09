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
/// </remarks>
public sealed partial class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private readonly StorageOptions _options = options.Value;
    private readonly string _root = Path.GetFullPath(options.Value.RootPath, AppContext.BaseDirectory);

    /// <summary>The directory files are written to, resolved to an absolute path.</summary>
    public string RootPath => _root;

    public async Task<StoredFile> SaveAsync(byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        var fileId = Guid.NewGuid().ToString("N");
        var path = ResolvePath(fileId);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a temporary name and move into place, so a reader never observes a half-written PDF.
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, path);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }

        return new StoredFile(fileId, $"{_options.PublicBaseUrl}/files/{fileId}", content.LongLength);
    }

    public Task<Stream?> OpenReadAsync(string fileId, CancellationToken cancellationToken = default)
    {
        // The id reaches us straight from a URL segment, so reject anything that is not one we issued.
        if (string.IsNullOrEmpty(fileId) || !FileIdPattern().IsMatch(fileId))
            return Task.FromResult<Stream?>(null);

        try
        {
            Stream stream = new FileStream(
                ResolvePath(fileId),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
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
}
