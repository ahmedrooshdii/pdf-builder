using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PdfBuilder.Api.Tests.Support;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// Story 2 on the real service: with a sufficient key the responses are what they were before keys
/// (FR-022), and the request's log lines name the client and key (FR-020, SC-009).
/// </summary>
public sealed class ClientIdentificationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_job_is_accepted_and_polled_exactly_as_before_with_a_key()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var product = api.ClientWithKey(await api.FullKeyAsync("ecommerce", Ct));

        using var accepted = await product.PostAsJsonAsync("/api/pdf/jobs", new { templateKey = "invoice", data = new { } }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(["jobId", "statusUrl"], body.EnumerateObject().Select(p => p.Name));
        var jobId = body.GetProperty("jobId").GetGuid();
        Assert.Equal($"/api/pdf/jobs/{jobId}", accepted.Headers.Location?.OriginalString);

        var job = await product.GetFromJsonAsync<JsonElement>($"/api/pdf/jobs/{jobId}", Ct);
        Assert.Equal(jobId, job.GetProperty("jobId").GetGuid());
        Assert.Equal("invoice", job.GetProperty("templateKey").GetString());

        using var missing = await product.GetAsync($"/api/pdf/jobs/{Guid.NewGuid()}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Templates_are_listed_and_read_exactly_as_before_with_a_key()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var product = api.ClientWithKey(await api.FullKeyAsync("ecommerce", Ct));

        var templates = await product.GetFromJsonAsync<JsonElement>("/api/templates", Ct);
        var invoice = await product.GetFromJsonAsync<JsonElement>("/api/templates/invoice", Ct);

        Assert.Contains(templates.EnumerateArray(), t => t.GetProperty("templateKey").GetString() == "invoice");
        Assert.Equal("invoice", invoice.GetProperty("templateKey").GetString());
    }

    [Fact]
    public async Task Each_clients_requests_are_attributed_to_it_in_the_logs()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        var ecommerce = await api.IssueKeyAsync("ecommerce", ["pdf:generate"], Ct);
        var erp = await api.IssueKeyAsync("erp", ["pdf:generate"], Ct);

        foreach (var key in new[] { ecommerce, erp })
        {
            using var product = api.ClientWithKey(key.GetProperty("key").GetString());
            await product.PostAsJsonAsync("/api/pdf/jobs", new { templateKey = "invoice", data = new { } }, Ct);
        }

        // Saving the job is logged by EF Core from inside the endpoint, so within the request's scope.
        foreach (var (clientId, key) in new[] { ("ecommerce", ecommerce), ("erp", erp) })
        {
            var keyId = key.GetProperty("keyId").GetString();
            Assert.Contains(api.Logs.Entries, entry =>
                entry.Scope.TryGetValue("ClientId", out var client) && Equals(client, clientId)
                && entry.Scope.TryGetValue("KeyId", out var id) && Equals(id, keyId));
        }
    }
}
