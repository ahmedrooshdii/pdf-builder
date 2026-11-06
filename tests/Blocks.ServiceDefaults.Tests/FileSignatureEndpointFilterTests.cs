using Blocks.ServiceDefaults.Files;
using Blocks.ServiceDefaults.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>
/// <c>RequireFileSignature</c> (research R3): what each kind of link gets, with the switch off and on, and that
/// every refusal looks exactly like a file that does not exist.
/// </summary>
public sealed class FileSignatureEndpointFilterTests
{
    private const string FileId = "a7e0495b2cd24ed9bb65b1b0f3a14c20";
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Host : IAsyncDisposable
    {
        private readonly WebApplication _app;

        public Host(bool requireSignature)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<TimeProvider>(Clock);
            builder.Services.AddSingleton(Options.Create(new BlocksFilesOptions { SigningKey = Key, RequireSignature = requireSignature }));
            builder.Services.AddSingleton<FileLinkSigner>();
            _app = builder.Build();
            _app.MapGet("/files/{fileId}", (string fileId) => fileId == FileId ? Results.Text("the pdf") : Results.NotFound())
                .RequireFileSignature();
            _app.StartAsync().GetAwaiter().GetResult();
            Signer = _app.Services.GetRequiredService<FileLinkSigner>();
            Client = _app.GetTestClient();
        }

        public ManualClock Clock { get; } = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));

        public FileLinkSigner Signer { get; }

        public HttpClient Client { get; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static async Task<(int Status, string Body)> GetAsync(Host host, string path)
    {
        using var response = await host.Client.GetAsync(path, Ct);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task With_the_switch_off_an_unsigned_link_and_a_valid_signed_link_are_served()
    {
        await using var host = new Host(requireSignature: false);
        var signed = host.Signer.Sign(FileId, host.Clock.Now);

        Assert.Equal((200, "the pdf"), await GetAsync(host, $"/files/{FileId}"));
        Assert.Equal((200, "the pdf"), await GetAsync(host, $"/files/{FileId}?{signed.Query}"));
    }

    [Fact]
    public async Task With_the_switch_on_only_a_valid_signed_link_is_served()
    {
        await using var host = new Host(requireSignature: true);
        var signed = host.Signer.Sign(FileId, host.Clock.Now);

        Assert.Equal((200, "the pdf"), await GetAsync(host, $"/files/{FileId}?{signed.Query}"));
        Assert.Equal(404, (await GetAsync(host, $"/files/{FileId}")).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_expired_altered_or_half_present_signature_is_refused_whichever_way_the_switch_is_set(bool requireSignature)
    {
        await using var host = new Host(requireSignature);
        var signed = host.Signer.Sign(FileId, host.Clock.Now);
        var expires = signed.Query.Split('&')[0];
        var sig = signed.Query.Split('&')[1];
        var altered = sig[..^1] + (sig[^1] == 'A' ? 'B' : 'A');

        var refused = new[]
        {
            $"/files/{FileId}?{expires}&{altered}",     // altered
            $"/files/{FileId}?{expires}",               // one parameter
            $"/files/{FileId}?{sig}",                   // the other
            $"/files/b{FileId[1..]}?{signed.Query}"     // another file
        };

        foreach (var path in refused)
            Assert.Equal(404, (await GetAsync(host, path)).Status);

        host.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(404, (await GetAsync(host, $"/files/{FileId}?{signed.Query}")).Status);
    }

    [Fact]
    public async Task Every_refusal_is_byte_for_byte_the_answer_for_a_file_that_does_not_exist()
    {
        await using var host = new Host(requireSignature: true);
        var signed = host.Signer.Sign(FileId, host.Clock.Now);

        using var missing = await host.Client.GetAsync("/files/00000000000000000000000000000000", Ct);
        var expected = (missing.ToString(), await missing.Content.ReadAsStringAsync(Ct));

        foreach (var path in new[] { $"/files/{FileId}", $"/files/{FileId}?expires=1&sig=x", $"/files/{FileId}?{signed.Query[..^2]}zz" })
        {
            using var refused = await host.Client.GetAsync(path, Ct);
            Assert.Equal(expected, (refused.ToString(), await refused.Content.ReadAsStringAsync(Ct)));
        }
    }
}
