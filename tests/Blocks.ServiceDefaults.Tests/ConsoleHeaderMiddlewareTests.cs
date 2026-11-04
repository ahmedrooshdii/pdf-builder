using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blocks.ServiceDefaults.Tests.Support;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>
/// FR-007 and SC-010: a change made with a console session must carry <c>X-Console: 1</c>, which a page
/// on another site cannot send. The session is made through the real sign-in, so the identity checked
/// is the one the cookie handler actually produces.
/// </summary>
public sealed class ConsoleHeaderMiddlewareTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Changes() => new() { "POST", "PATCH", "DELETE" };

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task A_session_change_without_the_header_is_refused_before_the_endpoint_runs(string method)
    {
        await using var host = await StudHost.StartAsync();
        var cookie = await host.SignInAsync(Ct);

        using var request = new HttpRequestMessage(new HttpMethod(method), "/templates/edit");
        using var response = await host.Session(cookie, consoleHeader: false).SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("CONSOLE_HEADER_REQUIRED", problem.GetProperty("code").GetString());
    }

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task A_session_change_with_the_header_runs(string method)
    {
        await using var host = await StudHost.StartAsync();
        var cookie = await host.SignInAsync(Ct);

        using var request = new HttpRequestMessage(new HttpMethod(method), "/templates/edit");
        using var response = await host.Session(cookie).SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_session_read_needs_no_header()
    {
        await using var host = await StudHost.StartAsync();
        var cookie = await host.SignInAsync(Ct);

        Assert.Equal("listed", await host.Session(cookie, consoleHeader: false).GetStringAsync("/templates", Ct));
    }

    [Fact]
    public async Task A_wrong_header_value_is_refused()
    {
        await using var host = await StudHost.StartAsync();
        var cookie = await host.SignInAsync(Ct);
        var client = host.Session(cookie, consoleHeader: false);
        client.DefaultRequestHeaders.Add("X-Console", "true");

        using var response = await client.PostAsync("/templates/edit", null, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Keys_are_never_affected()
    {
        await using var host = await StudHost.StartAsync();
        var key = host.Store.Seed("ecommerce", StudHost.Permissions, host.Clock.Now);

        Assert.Equal(HttpStatusCode.OK, (await host.Admin().PostAsync("/templates/edit", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client(key).PostAsync("/templates/edit", null, Ct)).StatusCode);
    }
}
