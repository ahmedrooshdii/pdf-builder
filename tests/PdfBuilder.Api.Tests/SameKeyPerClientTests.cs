using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PdfBuilder.Api.Tests.Support;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// Feature 003, SC-003 and SC-004: two clients can each have a template called the same, each
/// one's is its own, and the built-in templates belong to everybody.
/// </summary>
public sealed class SameKeyPerClientTests(PostgresFixture postgres)
{
    /// <summary>The one template the service ships with.</summary>
    private const string BuiltIn = "invoice";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HttpResponseMessage> CreateAsync(HttpClient client, string key, string html) =>
        await client.PostAsJsonAsync("/api/templates", new { templateKey = key, content = new { html, css = "", sampleJson = "{}" } }, Ct);

    private static async Task<JsonElement> GetAsync(HttpClient client, string key) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/templates/{key}", Ct);

    private static IEnumerable<string> Keys(JsonElement list) =>
        list.EnumerateArray().Select(t => t.GetProperty("templateKey").GetString()!);

    [Fact]
    public async Task Two_clients_can_each_own_a_template_with_the_same_key_and_change_them_independently()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var alpha = await api.ClientForAsync("alpha", Ct);
        using var beta = await api.ClientForAsync("beta", Ct);

        using var createdByAlpha = await CreateAsync(alpha, "statement", "<p>alpha</p>");
        using var createdByBeta = await CreateAsync(beta, "statement", "<p>beta</p>");

        Assert.Equal(HttpStatusCode.Created, createdByAlpha.StatusCode);
        Assert.Equal(HttpStatusCode.Created, createdByBeta.StatusCode);

        // Beta edits its own; alpha's does not move.
        using var edited = await beta.PutAsJsonAsync(
            "/api/templates/statement/draft", new { revision = 1, content = new { html = "<p>beta, edited</p>" } }, Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        var forAlpha = await GetAsync(alpha, "statement");
        var forBeta = await GetAsync(beta, "statement");
        Assert.Equal("<p>alpha</p>", forAlpha.GetProperty("draft").GetProperty("html").GetString());
        Assert.Equal(1, forAlpha.GetProperty("revision").GetInt32());
        Assert.Equal("<p>beta, edited</p>", forBeta.GetProperty("draft").GetProperty("html").GetString());

        // Each list has exactly one of them.
        Assert.Single(Keys(await alpha.GetFromJsonAsync<JsonElement>("/api/templates", Ct)), "statement");
        Assert.Single(Keys(await beta.GetFromJsonAsync<JsonElement>("/api/templates", Ct)), "statement");

        // Deleting alpha's leaves beta's.
        using var deleted = await alpha.DeleteAsync("/api/templates/statement", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal("<p>beta, edited</p>", (await GetAsync(beta, "statement")).GetProperty("draft").GetProperty("html").GetString());
    }

    [Fact]
    public async Task A_key_is_taken_in_any_letter_case_within_a_client_and_a_built_in_key_is_taken_for_everyone()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var alpha = await api.ClientForAsync("alpha", Ct);
        using var beta = await api.ClientForAsync("beta", Ct);
        (await CreateAsync(alpha, "statement", "<p>x</p>")).Dispose();

        using var sameClientOtherCase = await CreateAsync(alpha, "Statement", "<p>x</p>");
        using var otherClientOtherCase = await CreateAsync(beta, "STATEMENT", "<p>x</p>");

        Assert.Equal(HttpStatusCode.Conflict, sameClientOtherCase.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherClientOtherCase.StatusCode);

        foreach (var client in new[] { alpha, beta })
        {
            foreach (var key in new[] { BuiltIn, "Invoice" })
            {
                using var response = await CreateAsync(client, key, "<p>x</p>");
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            }
        }
    }

    [Fact]
    public async Task Every_client_sees_the_built_in_templates_and_can_copy_them_but_not_change_them()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var alpha = await api.ClientForAsync("alpha", Ct);
        using var beta = await api.ClientForAsync("beta", Ct);

        foreach (var client in new[] { alpha, beta })
        {
            Assert.Contains(BuiltIn, Keys(await client.GetFromJsonAsync<JsonElement>("/api/templates", Ct)));
            Assert.Equal("BuiltIn", (await GetAsync(client, BuiltIn)).GetProperty("source").GetString());

            using var changed = await client.PutAsJsonAsync(
                $"/api/templates/{BuiltIn}/draft", new { revision = 1, content = new { html = "<p>x</p>" } }, Ct);
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        }

        // Alpha's copy is alpha's: beta neither lists it nor can copy it.
        using var copied = await alpha.PostAsJsonAsync($"/api/templates/{BuiltIn}/duplicate", new { templateKey = "my-invoice" }, Ct);
        Assert.Equal(HttpStatusCode.Created, copied.StatusCode);
        Assert.Contains("my-invoice", Keys(await alpha.GetFromJsonAsync<JsonElement>("/api/templates", Ct)));
        Assert.DoesNotContain("my-invoice", Keys(await beta.GetFromJsonAsync<JsonElement>("/api/templates", Ct)));

        using var stolen = await beta.PostAsJsonAsync("/api/templates/my-invoice/duplicate", new { templateKey = "stolen" }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
    }

    // ---- Jobs render the submitting client's own template -------------------------------------------

    private static async Task<(ApiFactory Api, RecordingRenderer Renderer)> StartWithRendererAsync(PostgresFixture postgres)
    {
        var renderer = new RecordingRenderer();
        var api = await ApiFactory.StartAsync(postgres, services =>
            services.AddSingleton<PdfBuilder.Application.Abstractions.IPdfRenderer>(renderer));
        return (api, renderer);
    }

    private static async Task PublishAsync(HttpClient client, string key, string html)
    {
        using var created = await CreateAsync(client, key, html);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var published = await client.PostAsJsonAsync($"/api/templates/{key}/publish", new { revision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
    }

    [Fact]
    public async Task Each_client_renders_its_own_template_when_both_use_the_same_key()
    {
        PostgresFixture.SkipUnlessEnabled();
        var (api, renderer) = await StartWithRendererAsync(postgres);
        await using var _ = api;
        using var alpha = await api.ClientForAsync("alpha", Ct);
        using var beta = await api.ClientForAsync("beta", Ct);
        await PublishAsync(alpha, "statement", "<p>ALPHA-STATEMENT</p>");
        await PublishAsync(beta, "statement", "<p>BETA-STATEMENT</p>");

        var alphaJob = await ApiFactory.SubmitJobAsync(alpha, "statement", new { }, Ct);
        var betaJob = await ApiFactory.SubmitJobAsync(beta, "statement", new { }, Ct);

        Assert.Equal("Succeeded", (await ApiFactory.WaitForJobAsync(alpha, alphaJob, Ct)).GetProperty("status").GetString());
        Assert.Equal("Succeeded", (await ApiFactory.WaitForJobAsync(beta, betaJob, Ct)).GetProperty("status").GetString());

        // One render each, and neither used the other's template.
        Assert.Single(renderer.Html, html => html.Contains("ALPHA-STATEMENT", StringComparison.Ordinal));
        Assert.Single(renderer.Html, html => html.Contains("BETA-STATEMENT", StringComparison.Ordinal));
        Assert.Equal(2, renderer.Html.Count);
    }

    [Fact]
    public async Task A_client_without_its_own_template_renders_the_built_in_one_and_never_another_clients()
    {
        PostgresFixture.SkipUnlessEnabled();
        var (api, renderer) = await StartWithRendererAsync(postgres);
        await using var _ = api;
        using var alpha = await api.ClientForAsync("alpha", Ct);
        using var beta = await api.ClientForAsync("beta", Ct);
        await PublishAsync(alpha, "alpha-only", "<p>ALPHA-ONLY</p>");

        var invoiceData = JsonSerializer.Deserialize<JsonElement>(
            await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "templates", "invoice.sample.json"), Ct));
        var builtInJob = await ApiFactory.SubmitJobAsync(beta, "invoice", invoiceData, Ct);
        var foreignJob = await ApiFactory.SubmitJobAsync(beta, "alpha-only", new { }, Ct);

        var builtIn = await ApiFactory.WaitForJobAsync(beta, builtInJob, Ct);
        var foreign = await ApiFactory.WaitForJobAsync(beta, foreignJob, Ct);

        Assert.Equal("Succeeded", builtIn.GetProperty("status").GetString());
        Assert.Equal("Failed", foreign.GetProperty("status").GetString());
        Assert.Equal("TEMPLATE_NOT_FOUND", foreign.GetProperty("errorCode").GetString());
        Assert.DoesNotContain(renderer.Html, html => html.Contains("ALPHA-ONLY", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PdfBuilder.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("The repository root (PdfBuilder.slnx) was not found above the test output.");
    }

    [Fact]
    public async Task A_clients_own_template_with_a_built_in_key_shadows_the_built_in_one_for_that_client_only()
    {
        // FR-007. It cannot be created through the API (a built-in key is taken), so it is written to the
        // database directly: what a deployment holds when a template was authored before a built-in one with
        // the same key shipped.
        PostgresFixture.SkipUnlessEnabled();
        var (api, renderer) = await StartWithRendererAsync(postgres);
        await using var _ = api;
        using var alpha = await api.ClientForAsync("alpha", Ct);
        using var beta = await api.ClientForAsync("beta", Ct);

        var content = JsonSerializer.Serialize(new
        {
            html = "<p>ALPHA-SHADOWS-INVOICE</p>",
            css = "",
            sampleJson = "{}",
            options = new
            {
                format = "A4", width = (string?)null, height = (string?)null, landscape = false, printBackground = true,
                preferCssPageSize = true, margins = new { top = "10mm", right = "10mm", bottom = "10mm", left = "10mm" }
            }
        });
        await using (var connection = new Npgsql.NpgsqlConnection(api.Database.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var command = new Npgsql.NpgsqlCommand(
                $"INSERT INTO authored_templates (client_id, \"key\", created_at, draft, published, published_at, revision, updated_at) " +
                $"VALUES ('alpha', 'invoice', now(), '{content}'::jsonb, '{content}'::jsonb, now(), 2, now())", connection);
            await command.ExecuteNonQueryAsync(Ct);
        }

        // Each client sees one `invoice`: alpha's own, beta's the built-in one.
        var alphasList = await alpha.GetFromJsonAsync<JsonElement>("/api/templates", Ct);
        var betasList = await beta.GetFromJsonAsync<JsonElement>("/api/templates", Ct);
        Assert.Equal("Authored", Assert.Single(alphasList.EnumerateArray(), t => t.GetProperty("templateKey").GetString() == "invoice").GetProperty("source").GetString());
        Assert.Equal("BuiltIn", Assert.Single(betasList.EnumerateArray(), t => t.GetProperty("templateKey").GetString() == "invoice").GetProperty("source").GetString());

        // And a job renders the same way.
        var data = JsonSerializer.Deserialize<JsonElement>(
            await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "templates", "invoice.sample.json"), Ct));
        var alphasJob = await ApiFactory.SubmitJobAsync(alpha, "invoice", data, Ct);
        var betasJob = await ApiFactory.SubmitJobAsync(beta, "invoice", data, Ct);
        Assert.Equal("Succeeded", (await ApiFactory.WaitForJobAsync(alpha, alphasJob, Ct)).GetProperty("status").GetString());
        Assert.Equal("Succeeded", (await ApiFactory.WaitForJobAsync(beta, betasJob, Ct)).GetProperty("status").GetString());

        Assert.Single(renderer.Html, html => html.Contains("ALPHA-SHADOWS-INVOICE", StringComparison.Ordinal));
        Assert.Equal(2, renderer.Html.Count);
    }
}
