using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PdfBuilder.Api.Tests.Support;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// Feature 003, User Story 5 on the real pipeline: the operator works inside one client at a time. Without a
/// client a template or job call is refused; a client that does not exist is not found; a disabled one works.
/// </summary>
public sealed class ActingClientTests(PostgresFixture postgres)
{
    private const string ChooseAClient = "Choose a client: send X-Client-Id with the client to act as.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Every endpoint about templates or jobs, as a request that would reach it.</summary>
    private static List<(string Method, string Path)> ClientDataEndpoints(IServiceProvider services) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.TrimStart('/').StartsWith("api/templates", StringComparison.Ordinal)
                        || e.RoutePattern.RawText!.TrimStart('/').StartsWith("api/pdf/jobs", StringComparison.Ordinal))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => (method, Concrete(e.RoutePattern.RawText!))))
            .ToList();

    private static string Concrete(string pattern)
    {
        var path = "/" + pattern.TrimStart('/');
        while (path.IndexOf('{', StringComparison.Ordinal) is var start and >= 0)
        {
            var end = path.IndexOf('}', start);
            var value = path[start..end].Contains(":guid", StringComparison.Ordinal) ? Guid.NewGuid().ToString() : "invoice";
            path = path[..start] + value + path[(end + 1)..];
        }

        return path;
    }

    /// <summary>A body every one of these endpoints can bind, so the refusal under test is the one that answers.</summary>
    private static HttpRequestMessage Request(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT" or "PATCH")
            request.Content = JsonContent.Create(new
            {
                templateKey = "invoice",
                revision = 1,
                content = new { html = "<p>x</p>" },
                data = new { }
            });
        return request;
    }

    private static async Task AssertEveryEndpointAnswersAsync(
        IEnumerable<(string Method, string Path)> endpoints, HttpClient caller, HttpStatusCode status, string code)
    {
        var count = 0;
        foreach (var (method, path) in endpoints)
        {
            using var request = Request(method, path);
            using var response = await caller.SendAsync(request, Ct);

            Assert.True(response.StatusCode == status, $"{method} {path}: {(int)response.StatusCode} {response.StatusCode}, expected {(int)status}");
            Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
            count++;
        }

        // The enumeration must have found the endpoints, or "every endpoint" proves nothing.
        Assert.True(count >= 10, $"Only {count} template and job endpoints were found.");
    }

    [Fact]
    public async Task Every_template_and_job_endpoint_asks_the_operator_to_choose_a_client()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);

        await AssertEveryEndpointAnswersAsync(
            ClientDataEndpoints(api.Services), api.AdminClient(), HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        // And says what to do about it.
        using var response = await api.AdminClient().GetAsync("/api/templates", Ct);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").GetProperty("X-Client-Id");
        Assert.Equal([ChooseAClient], errors.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Every_template_and_job_endpoint_says_an_unregistered_client_does_not_exist()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var admin = api.AdminClient();
        admin.DefaultRequestHeaders.Add("X-Client-Id", "nobody");

        await AssertEveryEndpointAnswersAsync(ClientDataEndpoints(api.Services), admin, HttpStatusCode.NotFound, "CLIENT_NOT_FOUND");
    }

    [Fact]
    public async Task A_console_session_is_held_to_the_same_rules()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        var cookie = await api.SignInAsync(Ct);

        using var noClient = api.SessionClient(cookie);
        using var unknownClient = api.SessionClient(cookie, actingAs: "nobody");

        await AssertEveryEndpointAnswersAsync(ClientDataEndpoints(api.Services), noClient, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await AssertEveryEndpointAnswersAsync(ClientDataEndpoints(api.Services), unknownClient, HttpStatusCode.NotFound, "CLIENT_NOT_FOUND");
    }

    [Fact]
    public async Task The_operator_acting_as_a_client_gets_exactly_what_that_clients_own_key_gets()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var alpha = await api.ClientForAsync("alpha", Ct);
        using var beta = await api.ClientForAsync("beta", Ct);
        using var operatorAsAlpha = await api.AdminActingAsAsync("alpha", Ct);
        (await alpha.PostAsJsonAsync("/api/templates", new { templateKey = "statement" }, Ct)).Dispose();

        Assert.Equal(
            await alpha.GetStringAsync("/api/templates", Ct),
            await operatorAsAlpha.GetStringAsync("/api/templates", Ct));

        // What the operator creates there is alpha's.
        using var created = await operatorAsAlpha.PostAsJsonAsync("/api/templates", new { templateKey = "by-the-operator" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var alphasKeys = (await alpha.GetFromJsonAsync<JsonElement>("/api/templates", Ct)).EnumerateArray().Select(t => t.GetProperty("templateKey").GetString());
        var betasKeys = (await beta.GetFromJsonAsync<JsonElement>("/api/templates", Ct)).EnumerateArray().Select(t => t.GetProperty("templateKey").GetString());
        Assert.Contains("by-the-operator", alphasKeys);
        Assert.DoesNotContain("by-the-operator", betasKeys);

        // And jobs the same way: the operator's job is alpha's, and beta cannot read it.
        var jobId = await ApiFactory.SubmitJobAsync(operatorAsAlpha, "invoice", new { }, Ct);
        using var seenByAlpha = await alpha.GetAsync($"/api/pdf/jobs/{jobId}", Ct);
        using var seenByBeta = await beta.GetAsync($"/api/pdf/jobs/{jobId}", Ct);
        Assert.Equal(HttpStatusCode.OK, seenByAlpha.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, seenByBeta.StatusCode);
    }

    [Fact]
    public async Task A_disabled_client_still_works_for_the_operator_while_its_own_key_is_refused()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var beta = await api.ClientForAsync("beta", Ct);
        using var disabled = await api.AdminClient().PatchAsJsonAsync("/api/admin/clients/beta", new { disabled = true }, Ct);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        using var operatorAsBeta = api.AdminClient();
        operatorAsBeta.DefaultRequestHeaders.Add("X-Client-Id", "beta");

        using var listed = await operatorAsBeta.GetAsync("/api/templates", Ct);
        using var created = await operatorAsBeta.PostAsJsonAsync("/api/templates", new { templateKey = "prepared-early" }, Ct);
        using var refused = await beta.GetAsync("/api/templates", Ct);

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("CLIENT_DISABLED", (await refused.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Endpoints_that_are_not_about_one_clients_data_need_no_client()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        var cookie = await api.SignInAsync(Ct);
        using var admin = api.AdminClient();
        using var anonymous = api.ClientWithKey(null);
        using var session = api.SessionClient(cookie);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/clients", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/scopes", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/console/session", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/files/not-a-real-file-id", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health/live", Ct)).StatusCode);
    }
}
