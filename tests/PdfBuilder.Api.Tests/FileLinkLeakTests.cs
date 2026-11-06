using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PdfBuilder.Api.Tests.Support;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// SC-008 and FR-007: a signature is a credential for as long as it lives, so it must not be written
/// anywhere it would outlast that. Everything the service writes while links are issued, used, altered
/// and expired is searched for the signature and for the signing key.
/// </summary>
public sealed class FileLinkLeakTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Settings(string? hostingLevel = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Blocks:Files:RequireSignature"] = "true",
            ["Blocks:Files:UrlLifetime"] = "00:02:00",

            // Every category at the most verbose level; what appsettings.json pins is more specific, so it stays.
            ["Logging:LogLevel:Default"] = "Trace"
        };
        if (hostingLevel is not null)
            settings["Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics"] = hostingLevel;
        return settings;
    }

    private async Task<(string Written, string Signature)> RunAsync(Dictionary<string, string?> settings)
    {
        PostgresFixture.SkipUnlessEnabled();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var api = ApiFactory.StartOn(
            database,
            settings: settings,
            configure: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton<IPdfRenderer>(new RecordingRenderer());
            });
        using var shop = await api.ClientForAsync("shop", Ct);
        using var anonymous = api.CreateClient();

        var written = new StringBuilder();
        async Task NoteAsync(HttpResponseMessage response, bool isLinkResponse = false)
        {
            // What a caller is told is searched too, except the response that is itself the link.
            if (!isLinkResponse)
                written.AppendLine(response.ToString()).AppendLine(await response.Content.ReadAsStringAsync(Ct));
        }

        var jobId = await ApiFactory.SubmitJobAsync(shop, "invoice", new { }, Ct);
        var job = await ApiFactory.WaitForJobAsync(shop, jobId, Ct);
        var link = new Uri(job.GetProperty("fileUrl").GetString()!).PathAndQuery;
        var signature = link[(link.IndexOf("sig=", StringComparison.Ordinal) + 4)..];

        using (var download = await anonymous.GetAsync(link, Ct))
            await NoteAsync(download, isLinkResponse: true);

        using (var altered = await anonymous.GetAsync(link[..^1] + (link[^1] == 'A' ? 'B' : 'A'), Ct))
            await NoteAsync(altered);
        using (var unsigned = await anonymous.GetAsync($"/files/{job.GetProperty("fileId").GetString()}", Ct))
            await NoteAsync(unsigned);
        using (var status = await shop.GetAsync($"/api/pdf/jobs/{jobId}", Ct))
            await NoteAsync(status, isLinkResponse: true);        // the status is where the link is handed over

        clock.Now += TimeSpan.FromMinutes(5);
        using (var expired = await anonymous.GetAsync(link, Ct))
            await NoteAsync(expired);

        written.AppendLine(api.Logs.AllText());
        return (written.ToString(), signature);
    }

    [Fact]
    public async Task Neither_a_signature_nor_the_signing_key_is_written_anywhere_the_service_logs_or_answers()
    {
        var (written, signature) = await RunAsync(Settings());

        Assert.Contains("shop", written, StringComparison.Ordinal);      // the search saw the service's own output
        Assert.DoesNotContain("sig=", written, StringComparison.Ordinal);
        Assert.DoesNotContain(signature, written, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiFactory.FileSigningKey, written, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiFactory.FileSigningKey[..24], written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_pin_is_what_keeps_request_urls_out_of_the_log()
    {
        // The control: with the request log turned up, the signature is in it. So the test above is
        // proof that the pin works, not just that nothing happened to log it.
        var (written, signature) = await RunAsync(Settings(hostingLevel: "Information"));

        Assert.Contains(signature, written, StringComparison.Ordinal);
    }
}
