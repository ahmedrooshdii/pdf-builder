using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Clients;
using Blocks.ServiceDefaults.Tests.Support;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>
/// Story 3: how every non-success is classified and answered. Unknown, malformed, revoked and expired
/// keys are one indistinguishable refusal (FR-015); an outage is "retry", never "invalid" (FR-021).
/// </summary>
public sealed class ApiKeyAuthenticationHandlerTests
{
    private static readonly string[] Generate = ["pdf:generate"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> CallAsync(
        StudHost host, string? key, string path = "/generate")
    {
        var response = await host.Client(key).GetAsync(path, Ct);
        var body = response.Content.Headers.ContentType?.MediaType == "application/problem+json"
            ? await response.Content.ReadFromJsonAsync<JsonElement>(Ct)
            : default;
        return (response.StatusCode, body, response);
    }

    /// <summary>A refusal with its trace id removed: what must be identical between cases.</summary>
    private static string WithoutTraceId(JsonElement problem) =>
        JsonSerializer.Serialize(problem.EnumerateObject()
            .Where(p => p.Name != "traceId")
            .ToDictionary(p => p.Name, p => p.Value));

    [Fact]
    public async Task No_key_is_authentication_required_with_a_challenge()
    {
        await using var host = await StudHost.StartAsync();

        var (status, body, response) = await CallAsync(host, key: null);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("AUTH_REQUIRED", body.GetProperty("code").GetString());
        Assert.Equal("ApiKey", response.Headers.WwwAuthenticate.ToString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.2", body.GetProperty("type").GetString());
        Assert.Equal("Authentication required", body.GetProperty("title").GetString());
        Assert.Equal(401, body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Unknown_wrong_revoked_expired_and_malformed_keys_get_one_identical_refusal()
    {
        await using var host = await StudHost.StartAsync();
        var now = host.Clock.Now;
        var valid = host.Store.Seed("ecommerce", Generate, now);

        var cases = new Dictionary<string, string>
        {
            ["unknown"] = ApiKeyFormat.ClientKey("pdf", ApiKeyFormat.NewKeyId(), ApiKeyFormat.NewSecret()),
            ["wrong secret"] = valid[..^40] + ApiKeyFormat.NewSecret(),
            ["revoked"] = host.Store.Seed("ecommerce", Generate, now, revokedAt: now),
            ["expired"] = host.Store.Seed("ecommerce", Generate, now.AddDays(-2), expiresAt: now.AddDays(-1)),
            ["expiring now"] = host.Store.Seed("ecommerce", Generate, now.AddDays(-2), expiresAt: now),
            ["malformed"] = "banana",
            ["another block"] = "eml" + valid[3..],
            ["padded"] = " " + valid,
            ["oversized"] = new string('a', 10_000),
            ["quickstart's made-up key"] = "pdf_zzzzzzzz_notarealsecret0000000000000000000000"
        };

        var refusals = new Dictionary<string, string>();
        foreach (var (name, key) in cases)
        {
            var (status, body, response) = await CallAsync(host, key);

            Assert.True(status == HttpStatusCode.Unauthorized, $"{name}: {status}");
            Assert.Equal("ApiKey", response.Headers.WwwAuthenticate.ToString());
            refusals[name] = WithoutTraceId(body);

            var text = body.GetRawText();
            Assert.DoesNotContain(key.Trim(), text, StringComparison.Ordinal);
            if (key.Trim().Split('_') is [_, var keyId, _])
                Assert.DoesNotContain(keyId, text, StringComparison.Ordinal);
        }

        Assert.Single(refusals.Values.Distinct());
        Assert.Contains("\"code\":\"AUTH_INVALID_KEY\"", refusals["unknown"], StringComparison.Ordinal);
        Assert.Equal("generated", await host.Client(valid).GetStringAsync("/generate", Ct));
    }

    [Fact]
    public async Task A_header_sent_twice_is_an_invalid_key()
    {
        await using var host = await StudHost.StartAsync();
        var key = host.Store.Seed("ecommerce", Generate, host.Clock.Now);

        using var client = host.Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/generate");
        request.Headers.TryAddWithoutValidation("X-Api-Key", [key, key]);
        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("AUTH_INVALID_KEY", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_blank_header_is_an_invalid_key_not_a_missing_one()
    {
        await using var host = await StudHost.StartAsync();

        // The test client drops a header whose value is empty; a blank one reaches the handler, and an
        // empty one is malformed too (ApiKeyFormatTests).
        using var client = host.Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/generate");
        request.Headers.TryAddWithoutValidation("X-Api-Key", "   ");
        using var response = await client.SendAsync(request, Ct);

        Assert.Equal("AUTH_INVALID_KEY", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_disabled_clients_valid_key_is_client_disabled()
    {
        await using var host = await StudHost.StartAsync();
        var key = host.Store.Seed("ecommerce", Generate, host.Clock.Now, clientDisabled: true);

        var (status, body, _) = await CallAsync(host, key);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("CLIENT_DISABLED", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Client_disabled_never_confirms_a_key_id_to_someone_without_its_secret()
    {
        await using var host = await StudHost.StartAsync();
        var now = host.Clock.Now;
        var key = host.Store.Seed("ecommerce", Generate, now, clientDisabled: true);
        var revoked = host.Store.Seed("ecommerce", Generate, now, revokedAt: now);

        var (wrongSecret, wrongBody, _) = await CallAsync(host, key[..^40] + ApiKeyFormat.NewSecret());
        var (revokedStatus, revokedBody, _) = await CallAsync(host, revoked);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongSecret);
        Assert.Equal("AUTH_INVALID_KEY", wrongBody.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, revokedStatus);
        Assert.Equal("AUTH_INVALID_KEY", revokedBody.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_wrong_admin_key_is_an_invalid_key()
    {
        await using var host = await StudHost.StartAsync();

        var (status, body, _) = await CallAsync(host, ApiKeyFormat.NewAdminKey());

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("AUTH_INVALID_KEY", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_outage_is_storage_unavailable_with_retry_after_never_invalid_key()
    {
        await using var host = await StudHost.StartAsync();
        var key = host.Store.Seed("ecommerce", Generate, host.Clock.Now);
        host.Store.Unavailable = true;

        var (status, body, response) = await CallAsync(host, key);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("STORAGE_UNAVAILABLE", body.GetProperty("code").GetString());
        Assert.Equal("5", response.Headers.RetryAfter?.ToString());

        // The admin key needs no storage to be recognised.
        Assert.Equal("generated", await host.Admin().GetStringAsync("/generate", Ct));

        // An outage is not remembered: the next request after recovery succeeds.
        host.Store.Unavailable = false;
        Assert.Equal("generated", await host.Client(key).GetStringAsync("/generate", Ct));

        var warning = Assert.Single(host.Logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(key.Split('_')[1], warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(key, host.Logs.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Any_other_failure_while_checking_is_a_server_error_not_an_invalid_key()
    {
        var store = Substitute.For<IClientKeyStore>();
        store.FindKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("bug"));
        var key = ApiKeyFormat.ClientKey("pdf", ApiKeyFormat.NewKeyId(), ApiKeyFormat.NewSecret());

        await using var failing = await StudHost.StartAsync(customStore: store);
        using var response = await failing.Client(key).GetAsync("/generate", Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(failing.Logs.Entries, e => e.Level == LogLevel.Error && e.Exception?.Contains("bug", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Refusals_for_bad_keys_are_not_logged_at_warning_or_above()
    {
        await using var host = await StudHost.StartAsync();

        for (var i = 0; i < 20; i++)
            await CallAsync(host, ApiKeyFormat.ClientKey("pdf", ApiKeyFormat.NewKeyId(), ApiKeyFormat.NewSecret()));
        await CallAsync(host, "banana");
        await CallAsync(host, null);

        Assert.DoesNotContain(host.Logs.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Open_endpoints_answer_without_a_key_and_even_with_a_bad_one()
    {
        await using var host = await StudHost.StartAsync();

        Assert.Equal("open", await host.Client().GetStringAsync("/open", Ct));
        Assert.Equal("open", await host.Client("banana").GetStringAsync("/open", Ct));
    }

    [Fact]
    public async Task An_endpoint_that_says_nothing_still_needs_a_key()
    {
        await using var host = await StudHost.StartAsync();
        var key = host.Store.Seed("ecommerce", Generate, host.Clock.Now);

        var (status, body, _) = await CallAsync(host, key: null, path: "/plain");

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("AUTH_REQUIRED", body.GetProperty("code").GetString());
        Assert.Equal("plain", await host.Client(key).GetStringAsync("/plain", Ct));
    }

    /// <summary>SC-007: with the key cached, checking it costs far less than the 25 ms budget.</summary>
    [Fact]
    public async Task Checking_a_cached_key_is_well_inside_the_latency_budget()
    {
        await using var host = await StudHost.StartAsync();
        var key = host.Store.Seed("ecommerce", Generate, host.Clock.Now);
        using var client = host.Client(key);
        await client.GetStringAsync("/generate", Ct);

        var timings = new List<double>();
        for (var i = 0; i < 1000; i++)
        {
            var started = Stopwatch.GetTimestamp();
            await client.GetStringAsync("/generate", Ct);
            timings.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        timings.Sort();
        Assert.True(timings[949] < 25, $"p95 of a whole in-process request was {timings[949]:F2} ms");
        Assert.Equal(1, host.Store.Reads);
    }
}
