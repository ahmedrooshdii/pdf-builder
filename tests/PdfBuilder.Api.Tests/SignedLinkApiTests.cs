using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using PdfBuilder.Api.Tests.Support;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// Feature 005, US1 on the real pipeline: with signing required, every link a caller gets is signed and
/// expires, and anything else is the same bare 404 as a file that does not exist (FR-001 to FR-005, FR-012).
/// </summary>
public sealed class SignedLinkApiTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Dictionary<string, string?> SigningOn = new()
    {
        ["Blocks:Files:RequireSignature"] = "true",
        ["Blocks:Files:UrlLifetime"] = "00:02:00"
    };

    private static async Task<(ApiFactory Api, TestDatabase Database, TestClock Clock)> StartAsync(
        PostgresFixture postgres, Dictionary<string, string?>? settings = null)
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var database = await postgres.CreateDatabaseAsync();
        var api = ApiFactory.StartOn(
            database,
            settings: settings ?? SigningOn,
            configure: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton<IPdfRenderer>(new RecordingRenderer());
            });
        return (api, database, clock);
    }

    private static async Task<JsonElement> FinishedJobAsync(ApiFactory api, HttpClient client, CancellationToken ct)
    {
        var jobId = await ApiFactory.SubmitJobAsync(client, "invoice", new { }, ct);
        var job = await ApiFactory.WaitForJobAsync(client, jobId, ct);
        Assert.Equal("Succeeded", job.GetProperty("status").GetString());
        return job;
    }

    /// <summary>Status, content type, the headers that matter and the body: everything a guesser could compare.</summary>
    private static async Task<string> SnapshotAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        var headers = string.Join(
            ";",
            response.Headers.Concat(response.Content.Headers)
                .Where(h => h.Key is not ("Date" or "Content-Length"))
                .OrderBy(h => h.Key, StringComparer.Ordinal)
                .Select(h => $"{h.Key}={string.Join(",", h.Value)}"));
        return $"{(int)response.StatusCode}|{headers}|{body}";
    }

    private static string RelativeUrl(string absolute) => new Uri(absolute).PathAndQuery;

    [Fact]
    public async Task A_job_status_carries_a_signed_link_that_downloads_without_a_key_and_says_when_it_expires()
    {
        PostgresFixture.SkipUnlessEnabled();
        var (api, database, clock) = await StartAsync(postgres);
        await using var _ = database;
        await using var __ = api;
        using var shop = await api.ClientForAsync("shop", Ct);

        var job = await FinishedJobAsync(api, shop, Ct);
        var link = RelativeUrl(job.GetProperty("fileUrl").GetString()!);
        var query = QueryHelpers.ParseQuery(link[link.IndexOf('?', StringComparison.Ordinal)..]);
        var expiresAt = job.GetProperty("fileUrlExpiresAt").GetDateTimeOffset();

        Assert.True(query.ContainsKey("sig"));
        Assert.Equal(expiresAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), query["expires"]);
        Assert.Equal(clock.Now.AddMinutes(2), expiresAt, TimeSpan.FromSeconds(1));
        Assert.False(job.GetProperty("fileExpired").GetBoolean());

        using var anonymous = api.CreateClient();
        using var download = await anonymous.GetAsync(link, Ct);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.True(download.Headers.CacheControl?.NoStore);
        Assert.True(download.Headers.CacheControl?.Private);
        Assert.Equal("no-referrer", download.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("noindex", download.Headers.GetValues("X-Robots-Tag").Single());
    }

    [Fact]
    public async Task An_altered_missing_or_expired_signature_is_the_same_bare_404_as_a_file_that_does_not_exist()
    {
        PostgresFixture.SkipUnlessEnabled();
        var (api, database, clock) = await StartAsync(postgres);
        await using var _ = database;
        await using var __ = api;
        using var shop = await api.ClientForAsync("shop", Ct);
        using var erp = await api.ClientForAsync("erp", Ct);
        var shopJob = await FinishedJobAsync(api, shop, Ct);
        var erpJob = await FinishedJobAsync(api, erp, Ct);
        var link = RelativeUrl(shopJob.GetProperty("fileUrl").GetString()!);
        var path = link[..link.IndexOf('?', StringComparison.Ordinal)];
        var query = QueryHelpers.ParseQuery(link[link.IndexOf('?', StringComparison.Ordinal)..]);
        var expires = (string)query["expires"].Single()!;
        var sig = (string)query["sig"].Single()!;
        var otherFileId = erpJob.GetProperty("fileId").GetString()!;

        using var anonymous = api.CreateClient();
        using var unknown = await anonymous.GetAsync("/files/00000000000000000000000000000000", Ct);
        var expected = await SnapshotAsync(unknown);
        Assert.StartsWith("404|", expected, StringComparison.Ordinal);

        var refused = new[]
        {
            $"{path}?expires={expires}&sig={sig[..^1]}{(sig[^1] == 'A' ? 'B' : 'A')}",            // one character of the signature
            $"/files/{otherFileId}?expires={expires}&sig={sig}",                                   // another file, same client's neighbour
            $"{path[..^1]}{(path[^1] == '0' ? '1' : '0')}?expires={expires}&sig={sig}",           // one character of the file id
            $"{path}?expires={long.Parse(expires, System.Globalization.CultureInfo.InvariantCulture) + 3600}&sig={sig}",   // a later expiry
            path,                                                                                  // no parameters
            $"{path}?expires={expires}"                                                            // half of them
        };
        foreach (var url in refused)
        {
            using var response = await anonymous.GetAsync(url, Ct);
            Assert.Equal(expected, await SnapshotAsync(response));
        }

        // The link itself still works, until it expires; then it is the same answer.
        using (var works = await anonymous.GetAsync(link, Ct))
            Assert.Equal(HttpStatusCode.OK, works.StatusCode);

        clock.Now += TimeSpan.FromMinutes(3);
        using var expired = await anonymous.GetAsync(link, Ct);
        Assert.Equal(expected, await SnapshotAsync(expired));
    }

    [Fact]
    public async Task Every_status_read_makes_a_fresh_link_and_each_works_until_its_own_expiry()
    {
        PostgresFixture.SkipUnlessEnabled();
        var (api, database, clock) = await StartAsync(postgres);
        await using var _ = database;
        await using var __ = api;
        using var shop = await api.ClientForAsync("shop", Ct);
        var first = await FinishedJobAsync(api, shop, Ct);
        var jobId = first.GetProperty("jobId").GetGuid();

        clock.Now += TimeSpan.FromMinutes(1);
        var second = await shop.GetFromJsonAsync<JsonElement>($"/api/pdf/jobs/{jobId}", Ct);

        Assert.NotEqual(first.GetProperty("fileUrl").GetString(), second.GetProperty("fileUrl").GetString());
        Assert.True(second.GetProperty("fileUrlExpiresAt").GetDateTimeOffset() > first.GetProperty("fileUrlExpiresAt").GetDateTimeOffset());

        using var anonymous = api.CreateClient();
        foreach (var job in new[] { first, second })
        {
            using var response = await anonymous.GetAsync(RelativeUrl(job.GetProperty("fileUrl").GetString()!), Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // One minute and a bit later the first has expired and the second has not.
        clock.Now += TimeSpan.FromSeconds(90);
        using var firstLater = await anonymous.GetAsync(RelativeUrl(first.GetProperty("fileUrl").GetString()!), Ct);
        using var secondLater = await anonymous.GetAsync(RelativeUrl(second.GetProperty("fileUrl").GetString()!), Ct);
        Assert.Equal(HttpStatusCode.NotFound, firstLater.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondLater.StatusCode);
    }

    [Fact]
    public async Task With_signing_required_an_unsigned_link_to_a_real_file_is_refused()
    {
        PostgresFixture.SkipUnlessEnabled();
        var (api, database, _) = await StartAsync(postgres);
        await using var _ = database;
        await using var __ = api;
        using var shop = await api.ClientForAsync("shop", Ct);
        var job = await FinishedJobAsync(api, shop, Ct);

        using var anonymous = api.CreateClient();
        using var response = await anonymous.GetAsync($"/files/{job.GetProperty("fileId").GetString()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
