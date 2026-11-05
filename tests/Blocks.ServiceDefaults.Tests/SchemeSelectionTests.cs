using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blocks.ServiceDefaults.Tests.Support;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>
/// The <c>Blocks</c> policy scheme (research R4): a key always wins over a cookie, and a caller with
/// neither is refused exactly as before the console had a session (FR-010).
/// </summary>
public sealed class SchemeSelectionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_credential_is_refused_as_before_with_the_api_key_challenge()
    {
        await using var host = await StudHost.StartAsync();

        using var response = await host.Client().GetAsync("/plain", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("ApiKey", response.Headers.WwwAuthenticate.ToString());
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("AUTH_REQUIRED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_key_wins_over_a_session_cookie()
    {
        await using var host = await StudHost.StartAsync();
        var cookie = await host.SignInAsync(Ct);
        var key = host.Store.Seed("ecommerce", [StudHost.Generate], host.Clock.Now);

        var client = host.Session(cookie);
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        var caller = await client.GetFromJsonAsync<JsonElement>("/whoami", Ct);

        Assert.Equal("ecommerce", caller.GetProperty("clientId").GetString());
        Assert.False(caller.GetProperty("isAdmin").GetBoolean());

        // The key's own permissions apply, not the session's authority.
        using var edit = await client.PostAsync("/templates/edit", null, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
    }

    [Fact]
    public async Task An_invalid_key_beside_a_valid_cookie_is_still_an_invalid_key()
    {
        await using var host = await StudHost.StartAsync();
        var cookie = await host.SignInAsync(Ct);

        var client = host.Session(cookie);
        client.DefaultRequestHeaders.Add("X-Api-Key", "pdf_nope");
        using var response = await client.GetAsync("/plain", Ct);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("AUTH_INVALID_KEY", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_garbage_cookie_is_refused_without_a_redirect()
    {
        await using var host = await StudHost.StartAsync();

        using var response = await host.Session($"{StudHost.CookieName}=not-a-ticket").GetAsync("/plain", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("AUTH_REQUIRED", problem.GetProperty("code").GetString());
    }
}
