using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Exceptions;
using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;

namespace PdfBuilder.Infrastructure.Templates;

/// <summary>
/// Scriban adapter for <see cref="ITemplateEngine"/>.
/// </summary>
/// <remarks>
/// Thread-safe, and registered as a singleton. Parsed <see cref="Template"/> instances are immutable
/// and shared through the cache; the only mutable state is the <see cref="TemplateContext"/>, which
/// is created fresh for every render.
/// </remarks>
public sealed class ScribanTemplateEngine(IMemoryCache cache, IOptions<TemplateEngineOptions> options)
    : ITemplateEngine
{
    /// <summary>Upper bound on rendered HTML. A template that exceeds it has a runaway loop.</summary>
    private const int MaxOutputCharacters = 8 * 1024 * 1024;

    private const int MaxLoopIterations = 10_000;
    private const int MaxRecursionDepth = 100;

    private static readonly MemoryCacheEntryOptions CacheEntryOptions =
        new() { SlidingExpiration = TimeSpan.FromMinutes(30) };

    private readonly bool _strict = options.Value.StrictMode;

    public async Task<string> RenderAsync(
        string templateHtml,
        string jsonPayload,
        CancellationToken cancellationToken = default)
    {
        var template = GetOrParse(templateHtml);
        var model = ParseModel(jsonPayload);

        var context = CreateContext(cancellationToken);
        context.PushGlobal(model);

        try
        {
            return await template.RenderAsync(context);
        }
        catch (ScriptRuntimeException ex)
        {
            throw new TemplateRenderException($"Template runtime error: {ex.Message}", ex);
        }
    }

    private static ScriptObject ParseModel(string jsonPayload)
    {
        try
        {
            return JsonToScriban.Parse(jsonPayload);
        }
        catch (JsonException ex)
        {
            throw new TemplateRenderException($"Payload is not valid JSON: {ex.Message}", ex);
        }
    }

    private TemplateContext CreateContext(CancellationToken cancellationToken)
    {
        var context = new TemplateContext
        {
            CancellationToken = cancellationToken,

            // Guards against a template that never terminates. Without them a bad template pins a
            // core and holds a render slot until the Playwright timeout fires several steps later.
            LoopLimit = MaxLoopIterations,
            RecursiveLimit = MaxRecursionDepth,
            OutputLimit = MaxOutputCharacters,

            // Scriban truncates on overflow by default, which would silently produce a short
            // document. Failing is the only honest option: a truncated invoice is worse than none.
            OnOutputLimit = ScriptLimitBehavior.Throw,
            OnStringLimit = ScriptLimitBehavior.Throw,

            RegexTimeOut = TimeSpan.FromSeconds(1),

            // No TemplateLoader is assigned, so `{{ include }}` cannot reach the file system.
            TemplateLoader = null,

            // These three move together and define what a missing field does. Setting only
            // StrictVariables would be a trap: `{{ a }}` would render blank while `{{ a.b }}`
            // threw, which is the kind of inconsistency that only shows up in production.
            StrictVariables = _strict,
            EnableRelaxedMemberAccess = !_strict,
            EnableRelaxedTargetAccess = !_strict
        };

        // CurrentCulture is read-only; culture is a stack, like globals. Invariant keeps output
        // identical whatever locale the container happens to boot with.
        context.PushCulture(CultureInfo.InvariantCulture);

        return context;
    }

    /// <summary>
    /// Caches by content hash rather than by template id, so an edited file is reparsed
    /// automatically and two ids with identical bodies share a single parse.
    /// </summary>
    private Template GetOrParse(string html)
    {
        var key = $"scriban:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html)))}";

        // A factory that throws is not cached, so a broken template is reparsed once it is fixed.
        return cache.GetOrCreate(key, entry =>
        {
            entry.SetOptions(CacheEntryOptions);

            var parsed = Template.Parse(html);
            return parsed.HasErrors
                ? throw new TemplateRenderException($"Template syntax error: {string.Join("; ", parsed.Messages)}")
                : parsed;
        })!;
    }
}
