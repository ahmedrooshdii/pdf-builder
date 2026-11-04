using System.Net.Http.Json;
using System.Text.Json;
using Blocks.ServiceDefaults.Tests.Support;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>
/// Story 2: a caller is identified from its claims (<c>ICurrentClient</c>), and every log line of its
/// request names it (FR-020, SC-009).
/// </summary>
public sealed class CurrentClientTests
{
    private static readonly string[] AllPermissions = ["pdf:generate", "pdf:templates:read", "pdf:templates:write"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_client_key_is_its_client_its_key_and_its_permissions()
    {
        await using var host = await StudHost.StartAsync();
        var key = host.Store.Seed("ecommerce", ["pdf:generate", "pdf:templates:read"], host.Clock.Now);
        var keyId = key.Split('_')[1];

        var caller = await host.Client(key).GetFromJsonAsync<JsonElement>("/whoami", Ct);

        Assert.Equal("ecommerce", caller.GetProperty("clientId").GetString());
        Assert.Equal(keyId, caller.GetProperty("keyId").GetString());
        Assert.Equal(["pdf:generate", "pdf:templates:read"], caller.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()));
        Assert.False(caller.GetProperty("isAdmin").GetBoolean());
    }

    [Fact]
    public async Task The_admin_key_has_no_client_and_no_key_id_only_the_admin_authority()
    {
        await using var host = await StudHost.StartAsync();

        var caller = await host.Admin().GetFromJsonAsync<JsonElement>("/whoami", Ct);

        Assert.Equal(JsonValueKind.Null, caller.GetProperty("clientId").ValueKind);
        Assert.Equal(JsonValueKind.Null, caller.GetProperty("keyId").ValueKind);
        Assert.Equal(["admin"], caller.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()));
        Assert.True(caller.GetProperty("isAdmin").GetBoolean());
    }

    [Fact]
    public async Task Every_log_line_of_a_request_names_the_client_and_key_that_made_it()
    {
        await using var host = await StudHost.StartAsync();
        var ecommerce = host.Store.Seed("ecommerce", AllPermissions, host.Clock.Now);
        var erp = host.Store.Seed("erp", AllPermissions, host.Clock.Now);

        await host.Client(ecommerce).GetStringAsync("/generate", Ct);
        await host.Client(erp).GetStringAsync("/generate", Ct);
        await host.Admin().GetStringAsync("/generate", Ct);

        var lines = host.Logs.Entries.Where(e => e.Message == "generating").ToList();
        Assert.Equal(3, lines.Count);

        Assert.Equal("ecommerce", lines[0].Scope["ClientId"]);
        Assert.Equal(ecommerce.Split('_')[1], lines[0].Scope["KeyId"]);
        Assert.Equal("erp", lines[1].Scope["ClientId"]);
        Assert.Equal(erp.Split('_')[1], lines[1].Scope["KeyId"]);
        Assert.Equal(true, lines[2].Scope["Admin"]);
        Assert.False(lines[2].Scope.ContainsKey("ClientId"));
    }

    private static HttpClient Acting(HttpClient client, params string[] clientIds)
    {
        foreach (var id in clientIds)
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Client-Id", id);
        return client;
    }

    [Fact]
    public async Task The_admin_key_acts_as_the_client_named_in_x_client_id()
    {
        await using var host = await StudHost.StartAsync();

        var caller = await Acting(host.Admin(), "ecommerce").GetFromJsonAsync<JsonElement>("/whoami", Ct);

        Assert.Equal("ecommerce", caller.GetProperty("clientId").GetString());
        Assert.Equal(JsonValueKind.Null, caller.GetProperty("keyId").ValueKind);
        Assert.True(caller.GetProperty("isAdmin").GetBoolean());
    }

    [Fact]
    public async Task A_console_session_acts_as_the_client_named_in_x_client_id()
    {
        await using var host = await StudHost.StartAsync();
        var cookie = await host.SignInAsync(Ct);

        var caller = await Acting(host.Session(cookie), "erp").GetFromJsonAsync<JsonElement>("/whoami", Ct);

        Assert.Equal("erp", caller.GetProperty("clientId").GetString());
        Assert.True(caller.GetProperty("isAdmin").GetBoolean());
    }

    [Fact]
    public async Task A_client_key_ignores_x_client_id_even_when_it_is_malformed()
    {
        await using var host = await StudHost.StartAsync();
        var key = host.Store.Seed("erp", AllPermissions, host.Clock.Now);

        var named = await Acting(host.Client(key), "ecommerce").GetFromJsonAsync<JsonElement>("/whoami", Ct);
        Assert.Equal("erp", named.GetProperty("clientId").GetString());

        var malformed = await Acting(host.Client(key), "Bad_Id").GetFromJsonAsync<JsonElement>("/whoami", Ct);
        Assert.Equal("erp", malformed.GetProperty("clientId").GetString());
    }

    [Theory]
    [InlineData("Bad_Id")]
    [InlineData("1shop")]
    public async Task A_malformed_x_client_id_from_the_admin_is_refused_before_the_endpoint(string value)
    {
        await using var host = await StudHost.StartAsync();

        using var response = await Acting(host.Admin(), value).GetAsync("/generate", Ct);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("VALIDATION_FAILED", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("X-Client-Id", out _));
        Assert.DoesNotContain(host.Logs.Entries, e => e.Message == "generating");
    }

    [Fact]
    public async Task X_client_id_sent_twice_by_the_admin_is_refused()
    {
        await using var host = await StudHost.StartAsync();

        using var response = await Acting(host.Admin(), "ecommerce", "erp").GetAsync("/generate", Ct);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_admin_acting_as_a_client_is_logged_with_that_client()
    {
        await using var host = await StudHost.StartAsync();

        await Acting(host.Admin(), "ecommerce").GetStringAsync("/generate", Ct);

        var line = Assert.Single(host.Logs.Entries, e => e.Message == "generating");
        Assert.Equal(true, line.Scope["Admin"]);
        Assert.Equal("ecommerce", line.Scope["ClientId"]);
    }
}
