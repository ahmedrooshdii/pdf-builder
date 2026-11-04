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
}
