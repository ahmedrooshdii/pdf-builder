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
/// Feature 003, SC-001 and SC-002 on the real pipeline: another client's template or job answers exactly
/// as one that does not exist, and is left exactly as it was. Not 403: a 403 would say it is there.
/// </summary>
/// <remarks>
/// The comparison is before and after, on one service: client B asks about a key nobody has, then client A
/// creates it, then B asks the same again. The answers must be identical. Comparing B's answer for A's key
/// with B's answer for some other key would not do, because an error says which key it was about.
/// </remarks>
public sealed class CrossClientIsolationTests(PostgresFixture postgres)
{
    private const string Secret = "secret";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Case(string Route, Func<string, HttpRequestMessage> Build);

    private static HttpRequestMessage Json(HttpMethod method, string path, object body) =>
        new(method, path) { Content = JsonContent.Create(body) };

    /// <summary>Every template endpoint that is about one template by its key.</summary>
    private static readonly Case[] TemplatesByKey =
    [
        new("GET /api/templates/{templateKey}", key => new(HttpMethod.Get, $"/api/templates/{key}")),
        new("POST /api/templates/{templateKey}/duplicate", key =>
            Json(HttpMethod.Post, $"/api/templates/{key}/duplicate", new { templateKey = "stolen-copy" })),
        new("PUT /api/templates/{templateKey}/draft", key =>
            Json(HttpMethod.Put, $"/api/templates/{key}/draft", new { revision = 1, content = new { html = "<p>hijacked</p>" } })),
        new("POST /api/templates/{templateKey}/publish", key =>
            Json(HttpMethod.Post, $"/api/templates/{key}/publish", new { revision = 1 })),
        new("DELETE /api/templates/{templateKey}", key => new(HttpMethod.Delete, $"/api/templates/{key}"))
    ];

    /// <summary>
    /// Template endpoints that are not about one stored template: the list and creation are checked on
    /// their own below, and a preview reads nothing that is stored.
    /// </summary>
    private static readonly string[] TemplatesNotByKey = ["GET /api/templates/", "POST /api/templates/", "POST /api/templates/preview"];

    private static IEnumerable<string> RoutesUnder(IServiceProvider services, string prefix) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => $"{method} /{e.RoutePattern.RawText!.TrimStart('/')}"))
            .Where(route => route.Contains(" " + prefix, StringComparison.Ordinal));

    /// <summary>Status, content type and body with the trace id taken out, which differs on every request.</summary>
    private static async Task<string> SnapshotAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.Ordinal) == true && body.Length > 0)
        {
            var parsed = JsonSerializer.Deserialize<JsonElement>(body);
            if (parsed.ValueKind == JsonValueKind.Object)
                body = JsonSerializer.Serialize(parsed.EnumerateObject().Where(p => p.Name != "traceId").ToDictionary(p => p.Name, p => p.Value));
        }

        return $"{(int)response.StatusCode} {response.Content.Headers.ContentType}\n{body}";
    }

    private static async Task<Dictionary<string, string>> AskAsync(HttpClient caller, string key)
    {
        var answers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var testCase in TemplatesByKey)
        {
            using var request = testCase.Build(key);
            using var response = await caller.SendAsync(request, Ct);
            answers[testCase.Route] = await SnapshotAsync(response);
        }

        return answers;
    }

    private static async Task CreateAndPublishAsync(HttpClient owner, string key, string html)
    {
        using var created = await owner.PostAsJsonAsync(
            "/api/templates", new { templateKey = key, content = new { html, css = "", sampleJson = "{}" } }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var published = await owner.PostAsJsonAsync($"/api/templates/{key}/publish", new { revision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
    }

    [Fact]
    public async Task Every_template_endpoint_is_covered_by_these_tests()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);

        var covered = TemplatesByKey.Select(c => c.Route).Concat(TemplatesNotByKey).Order(StringComparer.Ordinal).ToList();
        var mapped = RoutesUnder(api.Services, "/api/templates").Order(StringComparer.Ordinal).ToList();

        // A template endpoint added later has to be put in one list or the other, on purpose.
        Assert.Equal(covered, mapped);
    }

    [Fact]
    public async Task Another_clients_template_answers_exactly_like_one_that_does_not_exist_and_is_left_untouched()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var alpha = await api.ClientForAsync("alpha", Ct);
        using var beta = await api.ClientForAsync("beta", Ct);

        // Nobody has the key: what "does not exist" looks like to beta, for every operation.
        var whenMissing = await AskAsync(beta, Secret);
        Assert.All(whenMissing, answer => Assert.StartsWith("404 ", answer.Value, StringComparison.Ordinal));

        await CreateAndPublishAsync(alpha, Secret, "<p>alpha only</p>");
        var before = await alpha.GetStringAsync($"/api/templates/{Secret}", Ct);

        // Now alpha has it. For beta, nothing may have changed.
        var whenAnotherClientHasIt = await AskAsync(beta, Secret);

        Assert.Equal(whenMissing, whenAnotherClientHasIt);
        Assert.Equal(before, await alpha.GetStringAsync($"/api/templates/{Secret}", Ct));

        // The duplicate beta asked for was never made, for either client.
        using var copyForBeta = await beta.GetAsync("/api/templates/stolen-copy", Ct);
        using var copyForAlpha = await alpha.GetAsync("/api/templates/stolen-copy", Ct);
        Assert.Equal(HttpStatusCode.NotFound, copyForBeta.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, copyForAlpha.StatusCode);
    }

    [Fact]
    public async Task Another_clients_template_is_not_in_the_list()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var alpha = await api.ClientForAsync("alpha", Ct);
        using var beta = await api.ClientForAsync("beta", Ct);
        await CreateAndPublishAsync(alpha, Secret, "<p>alpha only</p>");

        var forAlpha = await alpha.GetFromJsonAsync<JsonElement>("/api/templates", Ct);
        var forBeta = await beta.GetFromJsonAsync<JsonElement>("/api/templates", Ct);

        static IEnumerable<string> Keys(JsonElement list) => list.EnumerateArray().Select(t => t.GetProperty("templateKey").GetString()!);
        Assert.Contains(Secret, Keys(forAlpha));
        Assert.DoesNotContain(Secret, Keys(forBeta));
    }

    // ---- Jobs ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_job_endpoint_is_covered_by_these_tests()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);

        // A job endpoint added later (a list, a file link) has to be added here, with its cross-client case.
        Assert.Equal(
            ["GET /api/pdf/jobs/{jobId:guid}", "POST /api/pdf/jobs/"],
            RoutesUnder(api.Services, "/api/pdf/jobs").Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Another_clients_job_answers_exactly_like_a_job_that_never_existed()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres, services =>
            services.AddSingleton<PdfBuilder.Application.Abstractions.IPdfRenderer>(new RecordingRenderer()));
        using var alpha = await api.ClientForAsync("alpha", Ct);
        using var beta = await api.ClientForAsync("beta", Ct);
        var jobId = await ApiFactory.SubmitJobAsync(alpha, "invoice", new { }, Ct);

        // Pending, then finished: the answer for beta is the same at every stage.
        var stages = new List<(string Owner, string Other)>();
        foreach (var stage in new[] { "pending-or-done", "done" })
        {
            if (stage == "done")
                await ApiFactory.WaitForJobAsync(alpha, jobId, Ct);

            using var forOwner = await alpha.GetAsync($"/api/pdf/jobs/{jobId}", Ct);
            Assert.Equal(HttpStatusCode.OK, forOwner.StatusCode);

            var neverExisted = Guid.NewGuid();
            using var theirs = await beta.GetAsync($"/api/pdf/jobs/{jobId}", Ct);
            using var missing = await beta.GetAsync($"/api/pdf/jobs/{neverExisted}", Ct);

            // An error may say which id it was about, so compare with the ids taken out.
            var otherClientsJob = (await SnapshotAsync(theirs)).Replace(jobId.ToString(), "{id}", StringComparison.OrdinalIgnoreCase);
            var unknownJob = (await SnapshotAsync(missing)).Replace(neverExisted.ToString(), "{id}", StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("404 ", unknownJob, StringComparison.Ordinal);
            Assert.Equal(unknownJob, otherClientsJob);
            stages.Add((stage, otherClientsJob));
        }

        // Alpha still reads its own, finished.
        var finished = await alpha.GetFromJsonAsync<JsonElement>($"/api/pdf/jobs/{jobId}", Ct);
        Assert.Equal("Succeeded", finished.GetProperty("status").GetString());
    }
}
