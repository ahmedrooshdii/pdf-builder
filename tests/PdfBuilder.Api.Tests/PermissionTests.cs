using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PdfBuilder.Api.Tests.Support;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// Story 5 on the real endpoints: the table in contracts/authentication.md, endpoint by endpoint
/// (FR-016, FR-017), and a guard that no endpoint relies on authentication alone.
/// </summary>
public sealed class PermissionTests(PostgresFixture postgres)
{
    private const string Generate = "pdf:generate";
    private const string Read = "pdf:templates:read";
    private const string Write = "pdf:templates:write";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>contracts/authentication.md: the permission each endpoint needs.</summary>
    private static readonly Dictionary<string, string> Contract = new(StringComparer.Ordinal)
    {
        ["POST /api/pdf/jobs/"] = Generate,
        ["GET /api/pdf/jobs/{jobId:guid}"] = Generate,
        ["GET /api/templates/"] = Read,
        ["GET /api/templates/{templateKey}"] = Read,
        ["POST /api/templates/"] = Write,
        ["POST /api/templates/{templateKey}/duplicate"] = Write,
        ["PUT /api/templates/{templateKey}/draft"] = Write,
        ["POST /api/templates/{templateKey}/publish"] = Write,
        ["DELETE /api/templates/{templateKey}"] = Write,
        ["POST /api/templates/preview"] = Write,

        // 004 contracts/console-session.md: a console session and nothing else, not even the admin key.
        ["GET /api/console/session/"] = "blocks:console-session"
    };

    private static IEnumerable<(string Route, IReadOnlyList<string> Policies, RouteEndpoint Endpoint)> NonAnonymousEndpoints(IServiceProvider services) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(method => (
                $"{method} /{e.RoutePattern.RawText!.TrimStart('/')}",
                (IReadOnlyList<string>)e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).OfType<string>().ToList(),
                e)));

    [Fact]
    public async Task Every_endpoint_outside_admin_needs_exactly_the_permission_the_contract_names()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);

        var endpoints = NonAnonymousEndpoints(api.Services)
            .Where(e => !e.Route.Contains(" /api/admin/", StringComparison.Ordinal))
            .ToDictionary(e => e.Route, e => e.Policies);

        Assert.Equal(Contract.Keys.Order(StringComparer.Ordinal), endpoints.Keys.Order(StringComparer.Ordinal));
        foreach (var (route, permission) in Contract)
            Assert.True(endpoints[route].SequenceEqual([permission]), $"{route} needs {string.Join(", ", endpoints[route])}");

        // The management API needs the admin authority, everywhere.
        Assert.All(
            NonAnonymousEndpoints(api.Services).Where(e => e.Route.Contains(" /api/admin/", StringComparison.Ordinal)),
            e => Assert.Contains("admin", e.Policies));
    }

    [Fact]
    public async Task A_generation_key_cannot_touch_templates_and_nothing_changes()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var generator = api.ClientWithKey((await api.IssueKeyAsync("ecommerce", [Generate], Ct)).GetProperty("key").GetString());
        using var author = api.ClientWithKey(await api.FullKeyAsync("authoring", Ct));
        using var created = await author.PostAsJsonAsync("/api/templates", new { templateKey = "mine" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var before = await TemplateRowAsync(api);

        var attempts = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, "/api/templates", null),
            (HttpMethod.Post, "/api/templates", new { templateKey = "other" }),
            (HttpMethod.Put, "/api/templates/mine/draft", new { revision = 1, content = new { html = "<p>changed</p>" } }),
            (HttpMethod.Post, "/api/templates/mine/publish", new { revision = 1 }),
            (HttpMethod.Delete, "/api/templates/mine", null)
        };

        foreach (var (method, path, body) in attempts)
        {
            using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
            using var response = await generator.SendAsync(request, Ct);

            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{method} {path}: {response.StatusCode}");
            Assert.Equal("AUTH_SCOPE_MISSING", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
        }

        Assert.Equal(before, await TemplateRowAsync(api));
    }

    [Fact]
    public async Task A_template_reading_key_reads_templates_but_cannot_submit_jobs()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var reader = api.ClientWithKey((await api.IssueKeyAsync("viewer", [Read], Ct)).GetProperty("key").GetString());

        using var list = await reader.GetAsync("/api/templates", Ct);
        using var job = await reader.PostAsJsonAsync("/api/pdf/jobs", new { templateKey = "invoice", data = new { } }, Ct);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, job.StatusCode);
        Assert.Equal("AUTH_SCOPE_MISSING", (await job.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task No_client_key_can_manage_clients_and_the_admin_key_can_do_everything()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var full = api.ClientWithKey(await api.FullKeyAsync("ecommerce", Ct));
        using var admin = api.AdminClient();
        using var acting = await api.AdminActingAsAsync("ecommerce", Ct);   // templates and jobs belong to a client

        using var refused = await full.GetAsync("/api/admin/clients", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("AUTH_SCOPE_MISSING", (await refused.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.Accepted, (await acting.PostAsJsonAsync("/api/pdf/jobs", new { templateKey = "invoice", data = new { } }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await acting.GetAsync("/api/templates", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await acting.PostAsJsonAsync("/api/templates", new { templateKey = "by-admin" }, Ct)).StatusCode);
    }

    private static async Task<string> TemplateRowAsync(ApiFactory api)
    {
        await using var connection = new NpgsqlConnection(api.Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT coalesce(string_agg(row_to_json(t)::text, '' ORDER BY t.\"key\"), '') FROM authored_templates t", connection);
        return (string)(await command.ExecuteScalarAsync(Ct))!;
    }
}
