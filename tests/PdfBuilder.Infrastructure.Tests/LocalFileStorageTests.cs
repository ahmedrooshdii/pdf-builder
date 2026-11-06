using System.Text;
using Microsoft.Extensions.Options;
using PdfBuilder.Infrastructure.Storage;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Infrastructure.Tests;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pdf-builder-files-{Guid.NewGuid():N}");
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly LocalFileStorage _storage;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public LocalFileStorageTests() =>
        _storage = new LocalFileStorage(Options.Create(new StorageOptions { RootPath = _root }), _clock);

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
    public async Task Records_the_creation_time_as_the_files_last_write_time()
    {
        var stored = await _storage.SaveAsync([1], "application/pdf", Ct);

        Assert.Equal(_clock.Now, stored.CreatedAt);
        Assert.Equal(_clock.Now.UtcDateTime, File.GetLastWriteTimeUtc(PathOf(stored.FileId)));
    }

    [Fact]
    public async Task Says_whether_a_file_exists_and_false_for_ids_it_did_not_issue()
    {
        var stored = await _storage.SaveAsync([1], "application/pdf", Ct);

        Assert.True(await _storage.ExistsAsync(stored.FileId, Ct));
        Assert.False(await _storage.ExistsAsync(Guid.NewGuid().ToString("N"), Ct));
        Assert.False(await _storage.ExistsAsync("../../etc/passwd", Ct));
        Assert.False(await _storage.ExistsAsync("", Ct));
    }

    [Fact]
    public async Task Deletes_files_created_before_the_cutoff_and_keeps_younger_ones()
    {
        var old = await _storage.SaveAsync([1], "application/pdf", Ct);
        _clock.Now += TimeSpan.FromDays(10);
        var young = await _storage.SaveAsync([2], "application/pdf", Ct);

        var deleted = await _storage.DeleteCreatedBeforeAsync(_clock.Now - TimeSpan.FromDays(5), Ct);

        Assert.Equal(1, deleted);
        Assert.False(await _storage.ExistsAsync(old.FileId, Ct));
        Assert.True(await _storage.ExistsAsync(young.FileId, Ct));
        Assert.Equal(0, await _storage.DeleteCreatedBeforeAsync(_clock.Now - TimeSpan.FromDays(5), Ct));
    }

    [Fact]
    public async Task Deletes_abandoned_temporary_files_older_than_an_hour_but_not_fresh_ones()
    {
        var stored = await _storage.SaveAsync([1], "application/pdf", Ct);
        var shard = Path.GetDirectoryName(PathOf(stored.FileId))!;
        var stale = Path.Combine(shard, "stale.tmp");
        var fresh = Path.Combine(shard, "fresh.tmp");
        await File.WriteAllBytesAsync(stale, [1], Ct);
        await File.WriteAllBytesAsync(fresh, [1], Ct);
        File.SetLastWriteTimeUtc(stale, _clock.Now.UtcDateTime - TimeSpan.FromHours(2));
        File.SetLastWriteTimeUtc(fresh, _clock.Now.UtcDateTime - TimeSpan.FromMinutes(5));

        await _storage.DeleteCreatedBeforeAsync(_clock.Now - TimeSpan.FromDays(30), Ct);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public async Task A_sweep_over_a_missing_root_deletes_nothing()
    {
        Assert.Equal(0, await _storage.DeleteCreatedBeforeAsync(_clock.Now, Ct));
    }

    [Fact]
    public async Task A_reader_that_opened_a_file_before_the_delete_can_still_read_it_or_the_delete_is_skipped()
    {
        var stored = await _storage.SaveAsync([1, 2, 3], "application/pdf", Ct);
        await using var reader = await _storage.OpenReadAsync(stored.FileId, Ct);
        Assert.NotNull(reader);

        await _storage.DeleteCreatedBeforeAsync(_clock.Now + TimeSpan.FromDays(1), Ct);

        using var buffer = new MemoryStream();
        await reader.CopyToAsync(buffer, Ct);
        Assert.Equal([1, 2, 3], buffer.ToArray());
    }

    private string PathOf(string fileId) => Path.Combine(_root, fileId[..2], $"{fileId}.pdf");

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
