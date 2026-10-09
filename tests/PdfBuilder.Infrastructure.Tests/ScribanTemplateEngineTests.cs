using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PdfBuilder.Domain.Exceptions;
using PdfBuilder.Infrastructure.Templates;

namespace PdfBuilder.Infrastructure.Tests;

public sealed class ScribanTemplateEngineTests
{
    private readonly ScribanTemplateEngine _engine = CreateEngine(strict: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ScribanTemplateEngine CreateEngine(bool strict) => new(
        new MemoryCache(new MemoryCacheOptions()),
        Options.Create(new TemplateEngineOptions { StrictMode = strict }));

    [Fact]
    public async Task Binds_payload_fields_into_the_template()
    {
        var html = await _engine.RenderAsync(
            "<h1>{{ customer.name }}</h1>",
            """{"customer":{"name":"Acme"}}""",
            Ct);

        Assert.Equal("<h1>Acme</h1>", html);
    }

    [Fact]
    public async Task Iterates_arrays()
    {
        var html = await _engine.RenderAsync(
            "{{ for item in items }}[{{ item }}]{{ end }}",
            """{"items":["a","b","c"]}""",
            Ct);

        Assert.Equal("[a][b][c]", html);
    }

    [Fact]
    public async Task Renders_money_without_binary_floating_point_drift()
    {
        var html = await _engine.RenderAsync("{{ amount }}", """{"amount":1234567890.12}""", Ct);

        Assert.Equal("1234567890.12", html);
    }

    [Fact]
    public async Task Renders_large_integers_in_full_rather_than_scientific_notation()
    {
        var html = await _engine.RenderAsync("{{ id }}", """{"id":9007199254740993}""", Ct);

        Assert.Equal("9007199254740993", html);
    }

    [Fact]
    public async Task Formats_numbers_the_same_whatever_locale_the_host_uses()
    {
        var html = await _engine.RenderAsync(
            """{{ amount | math.format "n2" }}""",
            """{"amount":1234.5}""",
            Ct);

        Assert.Equal("1,234.50", html);
    }

    // The three cases below are one decision seen from three angles: what a missing field does.
    // They move together with TemplateEngineOptions.StrictMode and must never disagree.

    [Theory]
    [InlineData("[{{ missing }}]")]
    [InlineData("[{{ missing.field }}]")]
    [InlineData("[{{ customer.missing.deeper }}]")]
    public async Task Lenient_mode_renders_a_missing_field_as_nothing(string template)
    {
        var html = await _engine.RenderAsync(template, """{"customer":{"name":"Acme"}}""", Ct);

        Assert.Equal("[]", html);
    }

    [Theory]
    [InlineData("{{ missing }}")]
    [InlineData("{{ missing.field }}")]
    public async Task Strict_mode_fails_the_job_and_names_the_missing_field(string template)
    {
        var engine = CreateEngine(strict: true);

        var exception = await Assert.ThrowsAsync<TemplateRenderException>(
            () => engine.RenderAsync(template, "{}", Ct));

        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_a_syntax_error_as_a_permanent_failure()
    {
        var exception = await Assert.ThrowsAsync<TemplateRenderException>(
            () => _engine.RenderAsync("{{ for x in }}", "{}", Ct));

        Assert.False(exception.IsTransient);
        Assert.Contains("syntax error", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"text\"")]
    public async Task Rejects_payloads_that_are_not_json_objects(string payload)
    {
        await Assert.ThrowsAsync<TemplateRenderException>(() => _engine.RenderAsync("<p>x</p>", payload, Ct));
    }

    [Fact]
    public async Task Cannot_reach_the_file_system_through_include()
    {
        // No TemplateLoader is configured, so `include` must fail rather than read a file.
        await Assert.ThrowsAsync<TemplateRenderException>(
            () => _engine.RenderAsync("{{ include 'secrets' }}", "{}", Ct));
    }

    [Fact]
    public async Task Stops_a_runaway_loop_instead_of_rendering_forever()
    {
        var exception = await Record.ExceptionAsync(
            () => _engine.RenderAsync("{{ while true }}x{{ end }}", "{}", Ct));

        Assert.NotNull(exception);
    }

    [Fact]
    public async Task Serves_concurrent_renders_of_one_cached_template_independently()
    {
        const string Template = "<p>{{ name }}</p>";

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(i =>
            _engine.RenderAsync(Template, $$"""{"name":"n{{i}}"}""", Ct)));

        Assert.Equal(32, results.Distinct().Count());
    }
}
