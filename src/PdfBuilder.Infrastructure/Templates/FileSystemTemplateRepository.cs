using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Infrastructure.Templates;

/// <summary>
/// Loads <c>{templateId}.html</c> from <see cref="TemplateStoreOptions.RootPath"/>, with page setup
/// taken from an optional sibling <c>{templateId}.options.json</c>.
/// </summary>
/// <remarks>
/// The file is read on every request. That is deliberate: the read is served from the OS page cache,
/// editing a template takes effect without a restart, and the expensive step - parsing - is already
/// cached by <see cref="ScribanTemplateEngine"/>. Swap this for a database repository when templates
/// need versioning, per-tenant ownership or an editing UI.
/// </remarks>
public sealed class FileSystemTemplateRepository(
    IOptions<TemplateStoreOptions> options,
    ILogger<FileSystemTemplateRepository> logger) : IPdfTemplateRepository
{
    private static readonly JsonSerializerOptions OptionsJson = new(JsonSerializerDefaults.Web);

    private readonly string _root = Path.GetFullPath(options.Value.RootPath, AppContext.BaseDirectory);

    /// <summary>The directory templates are read from, resolved to an absolute path.</summary>
    public string RootPath => _root;

    public async Task<PdfTemplate?> GetByIdAsync(string templateId, CancellationToken cancellationToken = default)
    {
        // Defence in depth: the command validator already rejects anything with a separator in it.
        if (string.IsNullOrEmpty(templateId) || Path.GetFileName(templateId) != templateId)
            return null;

        var htmlPath = Path.Combine(_root, $"{templateId}.html");
        if (!File.Exists(htmlPath))
            return null;

        var html = await File.ReadAllTextAsync(htmlPath, cancellationToken);
        var renderOptions = await ReadRenderOptionsAsync(templateId, cancellationToken);

        return new PdfTemplate(templateId, html, renderOptions);
    }

    private async Task<PdfRenderOptions> ReadRenderOptionsAsync(string templateId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_root, $"{templateId}.options.json");
        if (!File.Exists(path))
            return PdfRenderOptions.Default;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<PdfRenderOptions>(stream, OptionsJson, cancellationToken)
                   ?? PdfRenderOptions.Default;
        }
        catch (JsonException ex)
        {
            // A malformed sidecar must not take the template down: fall back to the defaults and say so.
            TemplateLog.MalformedRenderOptions(logger, ex, templateId);
            return PdfRenderOptions.Default;
        }
    }
}
