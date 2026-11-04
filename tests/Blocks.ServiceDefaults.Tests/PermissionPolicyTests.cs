using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blocks.ServiceDefaults.Tests.Support;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>
/// Story 5: each permission is independent, a missing one is <c>AUTH_SCOPE_MISSING</c> (not an invalid
/// key), the admin key satisfies every one, and no client key reaches the management API.
/// </summary>
public sealed class PermissionPolicyTests
{
    private static readonly string[] AdminOnly = ["admin"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, string, bool> Matrix() => new()
    {
        // key's permission, method, path, allowed
        { "pdf:generate", "GET", "/generate", true },
        { "pdf:generate", "GET", "/templates", false },
        { "pdf:generate", "POST", "/templates/edit", false },
        { "pdf:templates:read", "GET", "/templates", true },
        { "pdf:templates:read", "GET", "/generate", false },
        { "pdf:templates:read", "POST", "/templates/edit", false },
        { "pdf:templates:write", "POST", "/templates/edit", true },
        { "pdf:templates:write", "GET", "/templates", false },
        { "pdf:templates:write", "GET", "/generate", false }
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task A_key_reaches_exactly_what_its_permission_covers(string permission, string method, string path, bool allowed)
    {
        await using var host = await StudHost.StartAsync();
        var key = host.Store.Seed("ecommerce", [permission], host.Clock.Now);

        using var response = await host.Client(key).SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct);

        if (allowed)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return;
        }

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("AUTH_SCOPE_MISSING", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("GET", "/generate")]
    [InlineData("GET", "/templates")]
    [InlineData("POST", "/templates/edit")]
    [InlineData("GET", "/plain")]
    [InlineData("GET", "/api/admin/clients")]
    public async Task The_admin_key_satisfies_every_permission(string method, string path)
    {
        await using var host = await StudHost.StartAsync();

        using var response = await host.Admin().SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task No_client_key_can_manage_clients_whatever_its_permissions()
    {
        await using var host = await StudHost.StartAsync();
        var key = host.Store.Seed("ecommerce", StudHost.Permissions, host.Clock.Now);
        using var client = host.Client(key);

        using var list = await client.GetAsync("/api/admin/clients", Ct);
        using var register = await client.PostAsJsonAsync("/api/admin/clients", new { id = "mine", name = "Mine" }, Ct);

        foreach (var response in new[] { list, register })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("AUTH_SCOPE_MISSING", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
        }

        Assert.Null(await host.Store.GetClientAsync("mine", Ct));
    }

    [Fact]
    public async Task A_disabled_clients_key_is_client_disabled_even_where_it_lacks_the_permission()
    {
        await using var host = await StudHost.StartAsync();
        var key = host.Store.Seed("ecommerce", ["pdf:generate"], host.Clock.Now, clientDisabled: true);

        using var response = await host.Client(key).GetAsync("/templates", Ct);

        Assert.Equal("CLIENT_DISABLED", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_admin_permission_cannot_be_given_to_a_client_key()
    {
        await using var host = await StudHost.StartAsync();
        await host.RegisterAsync("ecommerce", Ct);

        using var response = await host.Admin().PostAsJsonAsync(
            "/api/admin/clients/ecommerce/keys", new { name = "sneaky", scopes = AdminOnly }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Store.Keys);
    }
}
