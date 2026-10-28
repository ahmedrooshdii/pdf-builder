using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Infrastructure.Templates;

/// <summary>
/// The built-in templates: <c>{templateKey}.html</c> in <see cref="TemplateStoreOptions.RootPath"/>,
/// with optional siblings <c>{templateKey}.options.json</c> (page setup) and
/// <c>{templateKey}.sample.json</c> (example payload).
/// </summary>
/// <remarks>
/// The files are read on every request. That is deliberate: the read is served from the OS page
/// cache, editing a template takes effect without a restart, and the expensive step - parsing - is
/// already cached by <see cref="ScribanTemplateEngine"/>. Built-in templates carry no separate
/// stylesheet: their CSS lives in the HTML, as it always has.
/// </remarks>
public sealed class FileSystemTemplateRepository(
    IOptions<TemplateStoreOptions> options,
    ILogger<FileSystemTemplateRepository> logger) : IBuiltInTemplateSource
{
    private static readonly JsonSerializerOptions OptionsJson = new(JsonSerializerDefaults.Web);

    private readonly string _root = Path.GetFullPath(options.Value.RootPath, AppContext.BaseDirectory);

    /// <summary>The directory templates are read from, resolved to an absolute path.</summary>
    public string RootPath => _root;

    public async Task<IReadOnlyList<BuiltInTemplate>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_root))
            return [];

        var templates = new List<BuiltInTemplate>();
        foreach (var path in Directory.EnumerateFiles(_root, "*.html"))
        {
            var key = Path.GetFileNameWithoutExtension(path);
            if (TemplateKey.IsValid(key) && await GetByKeyAsync(key, cancellationToken) is { } template)
                templates.Add(template);
        }

        return templates;
    }

    public async Task<BuiltInTemplate?> GetByKeyAsync(string templateKey, CancellationToken cancellationToken = default)
    {
        // Defence in depth: every caller has already validated the key against TemplateKey.
        if (string.IsNullOrEmpty(templateKey) || Path.GetFileName(templateKey) != templateKey)
            return null;

        var htmlPath = Path.Combine(_root, $"{templateKey}.html");
        if (!File.Exists(htmlPath))
            return null;

        var html = await File.ReadAllTextAsync(htmlPath, cancellationToken);
        var renderOptions = await ReadRenderOptionsAsync(templateKey, cancellationToken);
        var sample = await ReadSampleAsync(templateKey, cancellationToken);

        return new BuiltInTemplate(
            templateKey,
            new TemplateContent(html, Css: string.Empty, sample, renderOptions),
            File.GetLastWriteTimeUtc(htmlPath));
    }

    private async Task<string> ReadSampleAsync(string templateKey, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_root, $"{templateKey}.sample.json");
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : string.Empty;
    }

    private async Task<PdfRenderOptions> ReadRenderOptionsAsync(string templateKey, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_root, $"{templateKey}.options.json");
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
            TemplateLog.MalformedRenderOptions(logger, ex, templateKey);
            return PdfRenderOptions.Default;
        }
    }
}
