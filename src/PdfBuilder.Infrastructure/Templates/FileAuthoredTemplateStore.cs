using System.Text.Json;
using Microsoft.Extensions.Options;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Exceptions;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Infrastructure.Templates;

/// <summary>
/// Authored templates as one JSON file each, <c>{templateKey}.json</c>, in
/// <see cref="TemplateStoreOptions.AuthoredRootPath"/>.
/// </summary>
/// <remarks>
/// <para>
/// Writes go to a temporary file that is then renamed over the old one, so a crash mid-write
/// leaves the previous version intact rather than a truncated file. Read-check-write sequences are
/// serialised by a lock, which makes the revision check reliable within this process.
/// </para>
/// <para>
/// That lock is also this store's limit: two replicas writing the same directory could interleave.
/// Like the job store, it is built for the single instance this service runs as today; a database
/// implementation of <see cref="IAuthoredTemplateStore"/> is the step before scaling out.
/// </para>
/// </remarks>
public sealed class FileAuthoredTemplateStore(IOptions<TemplateStoreOptions> options) : IAuthoredTemplateStore, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _root = Path.GetFullPath(options.Value.AuthoredRootPath, AppContext.BaseDirectory);
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>The directory templates are stored in, resolved to an absolute path.</summary>
    public string RootPath => _root;

    public async Task<IReadOnlyList<AuthoredTemplate>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_root))
            return [];

        var templates = new List<AuthoredTemplate>();
        foreach (var path in Directory.EnumerateFiles(_root, "*.json"))
        {
            if (await ReadAsync(path, cancellationToken) is { } template)
                templates.Add(template);
        }

        return templates;
    }

    public Task<AuthoredTemplate?> GetAsync(string templateKey, CancellationToken cancellationToken = default) =>
        TemplateKey.IsValid(templateKey)
            ? ReadAsync(PathFor(templateKey), cancellationToken)
            : Task.FromResult<AuthoredTemplate?>(null);

    public async Task AddAsync(AuthoredTemplate authoredTemplate, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(PathFor(authoredTemplate.Key)))
                throw new TemplateKeyTakenException(authoredTemplate.Key);

            await WriteAsync(authoredTemplate, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task UpdateAsync(AuthoredTemplate authoredTemplate, int expectedRevision, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var stored = await ReadAsync(PathFor(authoredTemplate.Key), cancellationToken)
                         ?? throw new TemplateNotFoundException(authoredTemplate.Key);

            if (stored.Revision != expectedRevision)
                throw new StaleTemplateRevisionException(authoredTemplate.Key, expectedRevision, stored.Revision);

            await WriteAsync(authoredTemplate, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> DeleteAsync(string templateKey, CancellationToken cancellationToken = default)
    {
        if (!TemplateKey.IsValid(templateKey))
            return false;

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var path = PathFor(templateKey);
            if (!File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose() => _writeLock.Dispose();

    private string PathFor(string templateKey) => Path.Combine(_root, $"{templateKey}.json");

    private static async Task<AuthoredTemplate?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var stored = await JsonSerializer.DeserializeAsync<StoredTemplate>(stream, Json, cancellationToken);
            return stored?.ToDomain();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private async Task WriteAsync(AuthoredTemplate template, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);

        var path = PathFor(template.Key);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, StoredTemplate.From(template), Json, cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    /// <summary>
    /// The on-disk shape, kept apart from the domain type so the domain can change without
    /// silently changing the file format, and so the file stays readable by people.
    /// </summary>
    private sealed record StoredTemplate(
        string Key,
        int Revision,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? PublishedAt,
        TemplateContent Draft,
        TemplateContent? Published)
    {
        public static StoredTemplate From(AuthoredTemplate t) =>
            new(t.Key, t.Revision, t.CreatedAt, t.UpdatedAt, t.PublishedAt, t.Draft, t.Published);

        public AuthoredTemplate ToDomain() =>
            AuthoredTemplate.Restore(Key, Draft, Published, Revision, CreatedAt, UpdatedAt, PublishedAt);
    }
}
