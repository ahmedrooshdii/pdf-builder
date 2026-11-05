using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Tests.Support;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>
/// Story 1 against the in-memory stores: signing in with the admin key and only with it (FR-002), the
/// cookie's attributes and 12-hour absolute lifetime (FR-004), sign-out that defeats a copied cookie
/// (FR-005), a replaced admin key ending sessions (FR-006), the limit on failures (FR-008) and outages
/// (FR-009). contracts/console-session.md is the reference for every answer.
/// </summary>
public sealed class ConsoleSessionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();

    [Fact]
    public async Task Signing_in_with_the_admin_key_sets_a_strict_secure_http_only_cookie_for_twelve_hours()
    {
        await using var host = await StudHost.StartAsync();

        using var response = await host.PostSignInAsync(StudHost.AdminKey, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);

        var cookie = StudHost.SetCookieHeader(response)!.ToLowerInvariant();
        Assert.Contains("httponly", cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.Ordinal);
        Assert.Contains("samesite=strict", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/", cookie, StringComparison.Ordinal);
        Assert.Contains("expires=wed, 04 nov 2026 21:00:00 gmt", cookie, StringComparison.Ordinal);

        var session = Assert.Single(host.Sessions.Sessions);
        Assert.Equal(host.Clock.Now.AddHours(12), session.ExpiresAt);
    }

    [Fact]
    public async Task Signing_in_without_the_console_header_is_refused()
    {
        await using var host = await StudHost.StartAsync();

        using var response = await host.PostSignInAsync(StudHost.AdminKey, Ct, consoleHeader: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("CONSOLE_HEADER_REQUIRED", await CodeAsync(response));
        Assert.Empty(host.Sessions.Sessions);
    }

    public static TheoryData<string> NotTheAdminKey() => new TheoryData<string>
    {
        ApiKeyFormat.NewAdminKey(),
        ApiKeyFormat.ClientKey("pdf", ApiKeyFormat.NewKeyId(), ApiKeyFormat.NewSecret()),
        "",
        StudHost.AdminKey + "x",
        "adm_" + new string('a', 61)
    };

    [Theory]
    [MemberData(nameof(NotTheAdminKey))]
    public async Task Anything_but_the_admin_key_is_refused_like_an_invalid_key(string value)
    {
        await using var host = await StudHost.StartAsync();

        using var response = await host.PostSignInAsync(value, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("AUTH_INVALID_KEY", await CodeAsync(response));
        Assert.Null(StudHost.SessionCookie(response));
        Assert.Empty(host.Sessions.Sessions);
    }

    [Fact]
    public async Task A_client_key_with_every_permission_cannot_sign_in()
    {
        await using var host = await StudHost.StartAsync();
        var key = host.Store.Seed("ecommerce", StudHost.Permissions, host.Clock.Now);

        using var response = await host.PostSignInAsync(key, Ct);

        Assert.Equal("AUTH_INVALID_KEY", await CodeAsync(response));
    }

    [Fact]
    public async Task After_five_failures_in_a_minute_even_the_right_key_waits()
    {
        await using var host = await StudHost.StartAsync();
        for (var i = 0; i < 5; i++)
            (await host.PostSignInAsync("adm_wrong", Ct)).Dispose();

        using var refused = await host.PostSignInAsync(StudHost.AdminKey, Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("RATE_LIMITED", await CodeAsync(refused));
        Assert.Equal(TimeSpan.FromSeconds(60), refused.Headers.RetryAfter?.Delta);

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        using var accepted = await host.PostSignInAsync(StudHost.AdminKey, Ct);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }

    [Fact]
    public async Task The_session_says_when_it_ends_and_an_admin_key_is_not_a_session()
    {
        await using var host = await StudHost.StartAsync();
        var cookie = await host.SignInAsync(Ct);

        var session = await host.Session(cookie).GetFromJsonAsync<JsonElement>("/api/console/session", Ct);
        Assert.Equal(host.Clock.Now.AddHours(12), session.GetProperty("expiresAt").GetDateTimeOffset());

        using var withKey = await host.Admin().GetAsync("/api/console/session", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, withKey.StatusCode);
        Assert.Equal("AUTH_REQUIRED", await CodeAsync(withKey));
    }

    [Fact]
    public async Task A_session_carries_the_operators_authority_and_no_client()
    {
        await using var host = await StudHost.StartAsync();
        var cookie = await host.SignInAsync(Ct);
        var session = host.Session(cookie);

        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/admin/clients", Ct)).StatusCode);
        Assert.Equal("edited", await (await session.PostAsync("/templates/edit", null, Ct)).Content.ReadAsStringAsync(Ct));

        var caller = await session.GetFromJsonAsync<JsonElement>("/whoami", Ct);
        Assert.True(caller.GetProperty("isAdmin").GetBoolean());
        Assert.Equal(JsonValueKind.Null, caller.GetProperty("clientId").ValueKind);
        Assert.Equal(JsonValueKind.Null, caller.GetProperty("keyId").ValueKind);
    }

    [Fact]
    public async Task The_session_ends_twelve_hours_after_sign_in_however_much_it_is_used()
    {
        await using var host = await StudHost.StartAsync();
        var cookie = await host.SignInAsync(Ct);

        for (var hour = 1; hour < 12; hour++)
        {
            host.Clock.Advance(TimeSpan.FromHours(1));
            Assert.Equal(HttpStatusCode.OK, (await host.Session(cookie).GetAsync("/api/console/session", Ct)).StatusCode);
        }

        host.Clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(HttpStatusCode.OK, (await host.Session(cookie).GetAsync("/api/console/session", Ct)).StatusCode);

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        using var ended = await host.Session(cookie).GetAsync("/api/admin/clients", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, ended.StatusCode);
        Assert.Equal("AUTH_REQUIRED", await CodeAsync(ended));
    }

    [Fact]
    public async Task After_sign_out_a_copy_of_the_cookie_is_refused_and_cleared()
    {
        await using var host = await StudHost.StartAsync();
        var cookie = await host.SignInAsync(Ct);

        using var signOut = await host.Session(cookie).DeleteAsync("/api/console/session", Ct);
        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        Assert.NotNull(StudHost.SetCookieHeader(signOut));
        Assert.Null(StudHost.SessionCookie(signOut));
        Assert.NotNull(Assert.Single(host.Sessions.Sessions).RevokedAt);

        using var replayed = await host.Session(cookie).GetAsync("/api/admin/clients", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);
        Assert.Equal("AUTH_REQUIRED", await CodeAsync(replayed));
        Assert.Null(StudHost.SessionCookie(replayed));
    }

    [Fact]
    public async Task Signing_out_needs_the_console_header_and_succeeds_without_a_session()
    {
        await using var host = await StudHost.StartAsync();

        using var withoutHeader = await host.Client().DeleteAsync("/api/console/session", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, withoutHeader.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/console/session");
        request.Headers.Add("X-Console", "1");
        using var withoutSession = await host.Client().SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.NoContent, withoutSession.StatusCode);
    }

    [Fact]
    public async Task A_session_survives_a_restart_but_not_a_new_admin_key()
    {
        var sessions = new InMemoryConsoleSessionStore();
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        string cookie;
        await using (var first = await StudHost.StartAsync(sessions: sessions, clock: clock))
            cookie = await first.SignInAsync(Ct);

        await using (var restarted = await StudHost.StartAsync(sessions: sessions, clock: clock))
            Assert.Equal(HttpStatusCode.OK, (await restarted.Session(cookie).GetAsync("/api/console/session", Ct)).StatusCode);

        var newKeyHash = KeyHasher.HashAdminKey(ApiKeyFormat.NewAdminKey());
        await using var rotated = await StudHost.StartAsync(sessions: sessions, clock: clock, adminKeyHash: newKeyHash);
        using var refused = await rotated.Session(cookie).GetAsync("/api/admin/clients", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal("AUTH_REQUIRED", await CodeAsync(refused));
    }

    [Fact]
    public async Task Signing_out_on_one_instance_ends_the_session_on_another()
    {
        var sessions = new InMemoryConsoleSessionStore();
        await using var a = await StudHost.StartAsync(sessions: sessions);
        await using var b = await StudHost.StartAsync(sessions: sessions, clock: a.Clock);
        var cookie = await a.SignInAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, (await b.Session(cookie).GetAsync("/api/console/session", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await a.Session(cookie).DeleteAsync("/api/console/session", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.Session(cookie).GetAsync("/api/console/session", Ct)).StatusCode);
    }

    [Fact]
    public async Task During_an_outage_sign_in_answers_try_again_but_a_wrong_key_is_still_wrong()
    {
        await using var host = await StudHost.StartAsync();
        host.Sessions.Unavailable = true;

        using var wrong = await host.PostSignInAsync("adm_wrong", Ct);
        Assert.Equal("AUTH_INVALID_KEY", await CodeAsync(wrong));

        using var right = await host.PostSignInAsync(StudHost.AdminKey, Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, right.StatusCode);
        Assert.Equal("STORAGE_UNAVAILABLE", await CodeAsync(right));
        Assert.Equal(TimeSpan.FromSeconds(5), right.Headers.RetryAfter?.Delta);
        Assert.Null(StudHost.SessionCookie(right));
    }

    [Fact]
    public async Task During_an_outage_a_session_answers_try_again_and_keeps_its_cookie()
    {
        await using var host = await StudHost.StartAsync();
        var cookie = await host.SignInAsync(Ct);
        host.Sessions.Unavailable = true;

        using var check = await host.Session(cookie).GetAsync("/api/admin/clients", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, check.StatusCode);
        Assert.Equal("STORAGE_UNAVAILABLE", await CodeAsync(check));
        Assert.Null(StudHost.SetCookieHeader(check));

        using var signOut = await host.Session(cookie).DeleteAsync("/api/console/session", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, signOut.StatusCode);
        Assert.Null(StudHost.SetCookieHeader(signOut));

        host.Sessions.Unavailable = false;
        Assert.Equal(HttpStatusCode.OK, (await host.Session(cookie).GetAsync("/api/console/session", Ct)).StatusCode);
    }

    [Fact]
    public async Task Expired_sessions_are_deleted_a_day_after_they_end()
    {
        await using var host = await StudHost.StartAsync();
        await host.SignInAsync(Ct);

        host.Clock.Advance(TimeSpan.FromHours(12) + TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1));
        await host.SignInAsync(Ct);

        var remaining = Assert.Single(host.Sessions.Sessions);
        Assert.Equal(host.Clock.Now, remaining.CreatedAt);
    }

    [Fact]
    public async Task Neither_the_admin_key_nor_the_cookie_nor_the_session_id_appears_in_the_logs()
    {
        await using var host = await StudHost.StartAsync();
        (await host.PostSignInAsync("adm_wrong_value_for_the_log_check", Ct)).Dispose();
        var cookie = await host.SignInAsync(Ct);
        await host.Session(cookie).DeleteAsync("/api/console/session", Ct);

        var text = host.Logs.AllText();
        Assert.DoesNotContain(StudHost.AdminKey, text, StringComparison.Ordinal);
        Assert.DoesNotContain("adm_wrong_value_for_the_log_check", text, StringComparison.Ordinal);
        Assert.DoesNotContain(cookie.Split('=', 2)[1], text, StringComparison.Ordinal);

        Assert.Contains(host.Logs.Entries, e => e.Message.StartsWith("Console session started", StringComparison.Ordinal));
        Assert.Contains(host.Logs.Entries, e => e.Message == "Console session ended");
    }
}
