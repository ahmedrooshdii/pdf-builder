using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blocks.ServiceDefaults.Clients;
using MassTransit.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PdfBuilder.Api.Tests.Support;
using PdfBuilder.Contracts;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// Story 3 on the real pipeline: every protected endpoint refuses before doing anything, the
/// refusals are uniform, the open endpoints stay open, and an outage answers 503 (FR-014, FR-015,
/// FR-019, FR-021, SC-001, SC-003).
/// </summary>
public sealed class AuthenticationPipelineTests(PostgresFixture postgres)
{
    private static readonly string[] Generate = ["pdf:generate"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Every endpoint the service maps that is not anonymous, as a request that would reach it.</summary>
    private static List<(string Method, string Path)> ProtectedEndpoints(IServiceProvider services) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => (method, Concrete(endpoint.RoutePattern.RawText!))))
            .ToList();

    /// <summary>A route pattern with every parameter filled in, for example /api/pdf/jobs/{jobId:guid}.</summary>
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

    private static HttpRequestMessage Request(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT" or "PATCH")
            request.Content = JsonContent.Create(new { templateKey = "invoice", data = new { } });
        return request;
    }

    private static string WithoutTraceId(JsonElement problem) =>
        JsonSerializer.Serialize(problem.EnumerateObject()
            .Where(p => p.Name != "traceId")
            .ToDictionary(p => p.Name, p => p.Value));

    [Fact]
    public async Task Every_protected_endpoint_refuses_a_caller_without_a_key()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var anonymous = api.ClientWithKey(null);

        var endpoints = ProtectedEndpoints(api.Services);
        Assert.Contains(endpoints, e => e.Path.TrimEnd('/') == "/api/pdf/jobs");
        Assert.Contains(endpoints, e => e.Path.StartsWith("/api/admin/clients", StringComparison.Ordinal));

        foreach (var (method, path) in endpoints)
        {
            using var request = Request(method, path);
            using var response = await anonymous.SendAsync(request, Ct);

            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path}: {response.StatusCode}");
            Assert.Equal("ApiKey", response.Headers.WwwAuthenticate.ToString());
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("AUTH_REQUIRED", problem.GetProperty("code").GetString());
            Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
        }
    }

    [Fact]
    public async Task Unknown_malformed_revoked_and_expired_keys_are_refused_identically_and_nothing_is_done()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var admin = api.AdminClient();

        var revoked = await api.IssueKeyAsync("ecommerce", Generate, Ct);
        await admin.DeleteAsync($"/api/admin/clients/ecommerce/keys/{revoked.GetProperty("keyId").GetString()}", Ct);
        var expired = await api.IssueKeyAsync("ecommerce", Generate, Ct, expiresAt: DateTimeOffset.UtcNow.AddSeconds(1));
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);

        var harness = api.Services.GetRequiredService<ITestHarness>();
        var keys = new Dictionary<string, string>
        {
            ["unknown"] = "pdf_zzzzzzzz_notarealsecret0000000000000000000000",
            ["malformed"] = "banana",
            ["another block"] = "eml_k7f2a9c1_Q8vLx2Tn9WcZr4YpB6sK1dHf7MjG3aE0uVnR5tXo",
            ["revoked"] = revoked.GetProperty("key").GetString()!,
            ["expired"] = expired.GetProperty("key").GetString()!
        };

        var bodies = new List<string>();
        foreach (var (name, key) in keys)
        {
            using var product = api.ClientWithKey(key);
            using var response = await product.PostAsJsonAsync("/api/pdf/jobs", new { templateKey = "invoice", data = new { } }, Ct);

            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{name}: {response.StatusCode}");
            var text = await response.Content.ReadAsStringAsync(Ct);
            Assert.DoesNotContain(key, text, StringComparison.Ordinal);
            bodies.Add(WithoutTraceId(JsonDocument.Parse(text).RootElement));
        }

        Assert.Single(bodies.Distinct());
        Assert.Contains("AUTH_INVALID_KEY", bodies[0], StringComparison.Ordinal);

        // SC-001: no job recorded, no message published, for any refused call.
        await using var connection = new NpgsqlConnection(api.Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var count = new NpgsqlCommand("SELECT count(*) FROM pdf_jobs", connection);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync(Ct))!);
        Assert.False(await harness.Published.Any<IGeneratePdfRequest>(Ct));
    }

    [Fact]
    public async Task Health_and_file_downloads_stay_open()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var anonymous = api.ClientWithKey(null);

        using var live = await anonymous.GetAsync("/health/live", Ct);
        using var ready = await anonymous.GetAsync("/health/ready", Ct);
        using var file = await anonymous.GetAsync("/files/not-a-real-file-id", Ct);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, ready.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, file.StatusCode);
    }

    [Fact]
    public async Task An_outage_is_storage_unavailable_for_client_keys_and_admin_operations()
    {
        PostgresFixture.SkipUnlessEnabled();
        var down = Substitute.For<IClientKeyStore>();
        down.FindKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new StorageUnavailableException(new System.Net.Sockets.SocketException()));
        down.ListClientsAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new StorageUnavailableException(new System.Net.Sockets.SocketException()));

        await using var api = await ApiFactory.StartAsync(postgres, services => services.Replace(ServiceDescriptor.Scoped(_ => down)));

        using var product = api.ClientWithKey("pdf_k7f2a9c1_Q8vLx2Tn9WcZr4YpB6sK1dHf7MjG3aE0uVnR5tXo");
        using var job = await product.PostAsJsonAsync("/api/pdf/jobs", new { templateKey = "invoice", data = new { } }, Ct);
        using var admin = await api.AdminClient().GetAsync("/api/admin/clients", Ct);

        foreach (var response in new[] { job, admin })
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("5", response.Headers.RetryAfter?.ToString());
            Assert.Equal("STORAGE_UNAVAILABLE", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
        }
    }

    /// <summary>SC-008: a burst of invented keys costs one lookup per id and does not hold up a valid client.</summary>
    [Fact]
    public async Task A_burst_of_invented_keys_does_not_hold_up_valid_clients()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var valid = api.ClientWithKey(await api.FullKeyAsync("ecommerce", Ct));
        using var scanner = api.ClientWithKey(null);

        var invented = Enumerable.Range(0, 1000)
            .Select(i => $"pdf_{i:D8}_{new string('x', 40)}")
            .ToList();

        var burst = Task.WhenAll(invented.Select(async key =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/templates");
            request.Headers.Add("X-Api-Key", key);
            using var response = await scanner.SendAsync(request, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }));

        for (var i = 0; i < 20; i++)
        {
            using var response = await valid.GetAsync("/api/templates", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await burst;
    }
}
