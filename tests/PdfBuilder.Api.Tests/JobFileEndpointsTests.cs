using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PdfBuilder.Api.Tests.Support;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Infrastructure.Storage;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// Feature 005, US2 and US4 on the real pipeline: a fresh link and a direct download for a job the caller
/// owns, and the three refusals: not found, no file, expired.
/// </summary>
public sealed class JobFileEndpointsTests(PostgresFixture postgres)
{
    private static readonly byte[] Pdf = [0x25, 0x50, 0x44, 0x46];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Switch(bool on) => new()
    {
        ["Blocks:Files:RequireSignature"] = on ? "true" : "false",
        ["Blocks:Files:UrlLifetime"] = "00:02:00"
    };

    private sealed record Rig(ApiFactory Api, TestDatabase Database, TestClock Clock) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Api.DisposeAsync();
            await Database.DisposeAsync();
        }

        /// <summary>Where the service keeps its PDFs, so a test can delete one by hand.</summary>
        public string FileRoot => ((LocalFileStorage)Api.Services.GetRequiredService<IFileStorage>()).RootPath;

        public void DeleteFile(string fileId) => File.Delete(Path.Combine(FileRoot, fileId[..2], $"{fileId}.pdf"));
    }

    private async Task<Rig> StartAsync(bool requireSignature)
    {
        PostgresFixture.SkipUnlessEnabled();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var database = await postgres.CreateDatabaseAsync();
        var api = ApiFactory.StartOn(
            database,
            settings: Switch(requireSignature),
            configure: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton<IPdfRenderer>(new RecordingRenderer());
            });
        return new Rig(api, database, clock);
    }

    private static async Task<JsonElement> SucceededJobAsync(HttpClient client)
    {
        var id = await ApiFactory.SubmitJobAsync(client, "invoice", new { }, Ct);
        var job = await ApiFactory.WaitForJobAsync(client, id, Ct);
        Assert.Equal("Succeeded", job.GetProperty("status").GetString());
        return job;
    }

    private static async Task<JsonElement> FailedJobAsync(HttpClient client)
    {
        var id = await ApiFactory.SubmitJobAsync(client, "no-such-template", new { }, Ct);
        var job = await ApiFactory.WaitForJobAsync(client, id, Ct);
        Assert.Equal("Failed", job.GetProperty("status").GetString());
        return job;
    }

    /// <summary>Status, content type and body with the trace id and the ids taken out.</summary>
    private static async Task<string> SnapshotAsync(HttpResponseMessage response, params Guid[] ids)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.Ordinal) == true && body.Length > 0)
        {
            var parsed = JsonSerializer.Deserialize<JsonElement>(body);
            if (parsed.ValueKind == JsonValueKind.Object)
                body = JsonSerializer.Serialize(parsed.EnumerateObject().Where(p => p.Name != "traceId").ToDictionary(p => p.Name, p => p.Value));
        }

        var snapshot = $"{(int)response.StatusCode} {response.Content.Headers.ContentType}\n{body}";
        return ids.Aggregate(snapshot, (text, id) => text.Replace(id.ToString(), "{id}", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()!;

    // ---- file-url --------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_caller_gets_a_working_link_for_its_own_job_and_no_earlier_link_stops_working(bool requireSignature)
    {
        await using var rig = await StartAsync(requireSignature);
        using var shop = await rig.Api.ClientForAsync("shop", Ct);
        var job = await SucceededJobAsync(shop);
        var jobId = job.GetProperty("jobId").GetGuid();

        using var response = await shop.GetAsync($"/api/pdf/jobs/{jobId}/file-url", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var fresh = new Uri(body.GetProperty("fileUrl").GetString()!).PathAndQuery;
        if (requireSignature)
            Assert.NotEqual(JsonValueKind.Null, body.GetProperty("fileUrlExpiresAt").ValueKind);
        else
            Assert.Equal(JsonValueKind.Null, body.GetProperty("fileUrlExpiresAt").ValueKind);
        Assert.Equal(requireSignature, fresh.Contains("sig=", StringComparison.Ordinal));

        using var anonymous = rig.Api.CreateClient();
        Assert.Equal(Pdf, await anonymous.GetByteArrayAsync(fresh, Ct));
        Assert.Equal(Pdf, await anonymous.GetByteArrayAsync(new Uri(job.GetProperty("fileUrl").GetString()!).PathAndQuery, Ct));
    }

    [Fact]
    public async Task Asking_about_an_unknown_job_is_the_same_404_as_reading_one()
    {
        await using var rig = await StartAsync(requireSignature: true);
        using var shop = await rig.Api.ClientForAsync("shop", Ct);
        var unknown = Guid.NewGuid();

        using var read = await shop.GetAsync($"/api/pdf/jobs/{unknown}", Ct);
        using var link = await shop.GetAsync($"/api/pdf/jobs/{unknown}/file-url", Ct);
        using var file = await shop.GetAsync($"/api/pdf/jobs/{unknown}/file", Ct);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(await SnapshotAsync(read, unknown), await SnapshotAsync(link, unknown));
        Assert.Equal(await SnapshotAsync(read, unknown), await SnapshotAsync(file, unknown));
    }

    [Fact]
    public async Task A_job_that_failed_has_no_file_to_link_to()
    {
        await using var rig = await StartAsync(requireSignature: true);
        using var shop = await rig.Api.ClientForAsync("shop", Ct);
        var job = await FailedJobAsync(shop);
        var jobId = job.GetProperty("jobId").GetGuid();

        using var link = await shop.GetAsync($"/api/pdf/jobs/{jobId}/file-url", Ct);
        using var file = await shop.GetAsync($"/api/pdf/jobs/{jobId}/file", Ct);

        Assert.Equal(HttpStatusCode.Conflict, link.StatusCode);
        Assert.Equal("JOB_HAS_NO_FILE", await CodeAsync(link));
        Assert.Equal(HttpStatusCode.Conflict, file.StatusCode);
        Assert.Equal("JOB_HAS_NO_FILE", await CodeAsync(file));
        Assert.Equal(JsonValueKind.Null, job.GetProperty("fileUrl").ValueKind);
        Assert.False(job.GetProperty("fileExpired").GetBoolean());
    }

    [Fact]
    public async Task A_file_deleted_from_disk_is_gone_at_once_for_every_way_of_asking()
    {
        await using var rig = await StartAsync(requireSignature: true);
        using var shop = await rig.Api.ClientForAsync("shop", Ct);
        var job = await SucceededJobAsync(shop);
        var jobId = job.GetProperty("jobId").GetGuid();
        var fileId = job.GetProperty("fileId").GetString()!;
        var oldLink = new Uri(job.GetProperty("fileUrl").GetString()!).PathAndQuery;

        rig.DeleteFile(fileId);

        using var link = await shop.GetAsync($"/api/pdf/jobs/{jobId}/file-url", Ct);
        Assert.Equal(HttpStatusCode.Gone, link.StatusCode);
        Assert.Equal("FILE_EXPIRED", await CodeAsync(link));

        var read = await shop.GetFromJsonAsync<JsonElement>($"/api/pdf/jobs/{jobId}", Ct);
        Assert.True(read.GetProperty("fileExpired").GetBoolean());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("fileUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, read.GetProperty("fileUrlExpiresAt").ValueKind);
        Assert.Equal(fileId, read.GetProperty("fileId").GetString());

        using var file = await shop.GetAsync($"/api/pdf/jobs/{jobId}/file", Ct);
        Assert.Equal(HttpStatusCode.Gone, file.StatusCode);
        Assert.Equal("FILE_EXPIRED", await CodeAsync(file));

        using var anonymous = rig.Api.CreateClient();
        using var old = await anonymous.GetAsync(oldLink, Ct);
        Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
    }

    // ---- file --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_owner_downloads_its_pdf_with_its_key_whichever_way_the_switch_is_set(bool requireSignature)
    {
        await using var rig = await StartAsync(requireSignature);
        using var shop = await rig.Api.ClientForAsync("shop", Ct);
        var jobId = (await SucceededJobAsync(shop)).GetProperty("jobId").GetGuid();

        using var response = await shop.GetAsync($"/api/pdf/jobs/{jobId}/file", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("noindex", response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Equal(Pdf, await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task The_operator_downloads_from_the_console_and_a_request_without_the_session_is_refused()
    {
        await using var rig = await StartAsync(requireSignature: true);
        using var shop = await rig.Api.ClientForAsync("shop", Ct);
        var jobId = (await SucceededJobAsync(shop)).GetProperty("jobId").GetGuid();
        var cookie = await rig.Api.SignInAsync(Ct);

        using var console = rig.Api.SessionClient(cookie, actingAs: "shop");
        using var withSession = await console.GetAsync($"/api/pdf/jobs/{jobId}/file", Ct);
        Assert.Equal(HttpStatusCode.OK, withSession.StatusCode);
        Assert.Equal(Pdf, await withSession.Content.ReadAsByteArrayAsync(Ct));

        using var anonymous = rig.Api.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Client-Id", "shop");
        using var without = await anonymous.GetAsync($"/api/pdf/jobs/{jobId}/file", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, without.StatusCode);
    }
}
