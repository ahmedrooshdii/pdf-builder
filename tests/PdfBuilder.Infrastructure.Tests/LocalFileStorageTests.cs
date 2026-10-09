using System.Text;
using Microsoft.Extensions.Options;
using PdfBuilder.Infrastructure.Storage;

namespace PdfBuilder.Infrastructure.Tests;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pdf-builder-files-{Guid.NewGuid():N}");
    private readonly LocalFileStorage _storage;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public LocalFileStorageTests() =>
        _storage = new LocalFileStorage(Options.Create(new StorageOptions
        {
            RootPath = _root,
            PublicBaseUrl = "https://pdf.example.com/"
        }));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Round_trips_a_saved_file()
    {
        var content = Encoding.UTF8.GetBytes("%PDF-1.7 pretend");

        var stored = await _storage.SaveAsync(content, "application/pdf", Ct);

        await using var stream = await _storage.OpenReadAsync(stored.FileId, Ct);
        Assert.NotNull(stream);

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, Ct);

        Assert.Equal(content, buffer.ToArray());
        Assert.Equal(content.Length, stored.SizeBytes);
    }

    [Fact]
    public async Task Builds_a_download_url_without_a_doubled_slash()
    {
        var stored = await _storage.SaveAsync([1], "application/pdf", Ct);

        Assert.Equal($"https://pdf.example.com/files/{stored.FileId}", stored.Url);
    }

    [Fact]
    public async Task Leaves_no_temporary_file_behind()
    {
        await _storage.SaveAsync([1, 2, 3], "application/pdf", Ct);

        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Shards_files_across_directories()
    {
        var stored = await _storage.SaveAsync([1], "application/pdf", Ct);

        Assert.True(File.Exists(Path.Combine(_root, stored.FileId[..2], $"{stored.FileId}.pdf")));
    }

    [Fact]
    public async Task Returns_null_for_an_unknown_file()
    {
        Assert.Null(await _storage.OpenReadAsync(Guid.NewGuid().ToString("N"), Ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../../../etc/passwd")]
    [InlineData("..")]
    [InlineData("ABCDEF01234567890123456789ABCDEF")]    // uppercase is not a shape we issue
    [InlineData("short")]
    [InlineData("0123456789abcdef0123456789abcdef0")]   // one character too long
    public async Task Refuses_file_ids_it_did_not_issue(string fileId)
    {
        Assert.Null(await _storage.OpenReadAsync(fileId, Ct));
    }
}
