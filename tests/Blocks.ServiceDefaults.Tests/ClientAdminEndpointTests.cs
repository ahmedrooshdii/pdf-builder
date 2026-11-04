using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blocks.ServiceDefaults.Tests.Support;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>
/// The management API over the in-memory store: Story 1 (register, issue once), Story 4 (revoke,
/// disable) and the contract's codes. The same operations run against PostgreSQL in PdfBuilder.Api.Tests.
/// </summary>
public sealed class ClientAdminEndpointTests
{
    private static readonly string[] GenerateOnly = ["pdf:generate"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    [Fact]
    public async Task Registering_a_client_answers_201_with_its_location_and_lists_it()
    {
        await using var host = await StudHost.StartAsync();
        using var admin = host.Admin();

        using var created = await admin.PostAsJsonAsync("/api/admin/clients", new { id = "ecommerce", name = " E-commerce " }, Ct);
        await host.RegisterAsync("erp", Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/admin/clients/ecommerce", created.Headers.Location?.OriginalString);
        var client = await JsonAsync(created);
        Assert.Equal("E-commerce", client.GetProperty("name").GetString());
        Assert.Equal("Active", client.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, client.GetProperty("disabledAt").ValueKind);

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/admin/clients", Ct);
        Assert.Equal(["ecommerce", "erp"], list.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetString()));

        var one = await admin.GetFromJsonAsync<JsonElement>("/api/admin/clients/ecommerce", Ct);
        Assert.Equal("ecommerce", one.GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_taken_id_or_a_broken_rule_is_refused_with_its_code()
    {
        await using var host = await StudHost.StartAsync();
        using var admin = host.Admin();
        await host.RegisterAsync("ecommerce", Ct);

        using var taken = await admin.PostAsJsonAsync("/api/admin/clients", new { id = "ecommerce", name = "Again" }, Ct);
        using var invalid = await admin.PostAsJsonAsync("/api/admin/clients", new { id = "E-Commerce", name = "E" }, Ct);
        using var missing = await admin.GetAsync("/api/admin/clients/missing", Ct);

        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal("CLIENT_ID_TAKEN", Code(await JsonAsync(taken)));

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = await JsonAsync(invalid);
        Assert.Equal("VALIDATION_FAILED", Code(problem));
        Assert.True(problem.GetProperty("errors").TryGetProperty("id", out _));
        Assert.True(problem.TryGetProperty("traceId", out _));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("CLIENT_NOT_FOUND", Code(await JsonAsync(missing)));
    }

    [Fact]
    public async Task An_issued_key_is_shown_once_works_at_once_and_is_never_listed()
    {
        await using var host = await StudHost.StartAsync();
        using var admin = host.Admin();
        await host.RegisterAsync("ecommerce", Ct);

        using var issued = await admin.PostAsJsonAsync(
            "/api/admin/clients/ecommerce/keys", new { name = "production", scopes = GenerateOnly }, Ct);

        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        Assert.True(issued.Headers.CacheControl?.NoStore);
        var body = await JsonAsync(issued);
        var key = body.GetProperty("key").GetString()!;
        var keyId = body.GetProperty("keyId").GetString()!;
        Assert.StartsWith($"pdf_{keyId}_", key, StringComparison.Ordinal);
        Assert.Equal($"pdf_{keyId}_…", body.GetProperty("display").GetString());
        Assert.Equal("Active", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("lastUsedAt").ValueKind);

        using var product = host.Client(key);
        Assert.Equal("generated", await product.GetStringAsync("/generate", Ct));

        var listed = await admin.GetStringAsync("/api/admin/clients/ecommerce/keys", Ct);
        var secret = key[(key.LastIndexOf('_') + 1)..];
        Assert.DoesNotContain(secret, listed, StringComparison.Ordinal);
        Assert.DoesNotContain("\"key\"", listed, StringComparison.Ordinal);

        var stored = Assert.Single(host.Store.Keys);
        Assert.Equal(32, stored.SecretHash.Length);
        Assert.Equal(["pdf:generate"], stored.Scopes);
    }

    [Fact]
    public async Task Issuing_validates_the_request_and_needs_an_existing_client()
    {
        await using var host = await StudHost.StartAsync();
        using var admin = host.Admin();
        await host.RegisterAsync("ecommerce", Ct);

        using var noClient = await admin.PostAsJsonAsync(
            "/api/admin/clients/missing/keys", new { name = "production", scopes = GenerateOnly }, Ct);
        using var noScopes = await admin.PostAsJsonAsync(
            "/api/admin/clients/ecommerce/keys", new { name = "production", scopes = Array.Empty<string>() }, Ct);
        using var past = await admin.PostAsJsonAsync(
            "/api/admin/clients/ecommerce/keys",
            new { name = "production", scopes = GenerateOnly, expiresAt = host.Clock.Now.AddSeconds(-1) }, Ct);

        Assert.Equal("CLIENT_NOT_FOUND", Code(await JsonAsync(noClient)));
        Assert.Equal(HttpStatusCode.BadRequest, noScopes.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.True((await JsonAsync(past)).GetProperty("errors").TryGetProperty("expiresAt", out _));
        Assert.Empty(host.Store.Keys);
    }

    [Fact]
    public async Task Keys_are_listed_newest_first_with_their_state()
    {
        await using var host = await StudHost.StartAsync();
        await host.RegisterAsync("ecommerce", Ct);

        var first = await host.IssueAsync("ecommerce", ["pdf:generate"], Ct);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await host.IssueAsync("ecommerce", ["pdf:templates:read"], Ct, expiresAt: host.Clock.Now.AddMinutes(1));
        host.Clock.Advance(TimeSpan.FromMinutes(2));

        var list = await host.Admin().GetFromJsonAsync<JsonElement>("/api/admin/clients/ecommerce/keys", Ct);
        var items = list.GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(
            [second.GetProperty("keyId").GetString(), first.GetProperty("keyId").GetString()],
            items.Select(k => k.GetProperty("keyId").GetString()));
        Assert.Equal(["Expired", "Active"], items.Select(k => k.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Revoking_is_permanent_idempotent_and_takes_effect_on_this_instance_at_once()
    {
        await using var host = await StudHost.StartAsync();
        await host.RegisterAsync("ecommerce", Ct);
        var revoked = await host.IssueAsync("ecommerce", ["pdf:generate"], Ct);
        var other = await host.IssueAsync("ecommerce", ["pdf:generate"], Ct);
        var keyId = revoked.GetProperty("keyId").GetString();

        using var product = host.Client(revoked.GetProperty("key").GetString());
        Assert.Equal("generated", await product.GetStringAsync("/generate", Ct));    // now cached

        using var admin = host.Admin();
        using var first = await admin.DeleteAsync($"/api/admin/clients/ecommerce/keys/{keyId}", Ct);
        var revokedAt = Assert.Single(host.Store.Keys, k => k.KeyId == keyId).RevokedAt;
        host.Clock.Advance(TimeSpan.FromSeconds(5));
        using var again = await admin.DeleteAsync($"/api/admin/clients/ecommerce/keys/{keyId}", Ct);
        using var unknown = await admin.DeleteAsync("/api/admin/clients/ecommerce/keys/zzzzzzzz", Ct);
        using var otherClient = await admin.DeleteAsync($"/api/admin/clients/erp/keys/{keyId}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(revokedAt, Assert.Single(host.Store.Keys, k => k.KeyId == keyId).RevokedAt);
        Assert.Equal("KEY_NOT_FOUND", Code(await JsonAsync(unknown)));
        Assert.Equal("KEY_NOT_FOUND", Code(await JsonAsync(otherClient)));

        using var refused = await product.GetAsync("/generate", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal("AUTH_INVALID_KEY", Code(await JsonAsync(refused)));

        using var stillWorks = host.Client(other.GetProperty("key").GetString());
        Assert.Equal("generated", await stillWorks.GetStringAsync("/generate", Ct));

        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/admin/clients/ecommerce/keys", Ct);
        Assert.Contains(listed.GetProperty("items").EnumerateArray(), k => k.GetProperty("status").GetString() == "Revoked");
    }

    [Fact]
    public async Task Disabling_a_client_refuses_its_keys_and_enabling_it_lets_unrevoked_ones_back()
    {
        await using var host = await StudHost.StartAsync();
        await host.RegisterAsync("ecommerce", Ct);
        var key = (await host.IssueAsync("ecommerce", ["pdf:generate"], Ct)).GetProperty("key").GetString();
        using var product = host.Client(key);
        using var admin = host.Admin();
        Assert.Equal("generated", await product.GetStringAsync("/generate", Ct));

        using var disabled = await admin.PatchAsJsonAsync("/api/admin/clients/ecommerce", new { disabled = true }, Ct);
        var disabledAt = (await JsonAsync(disabled)).GetProperty("disabledAt").GetDateTimeOffset();
        host.Clock.Advance(TimeSpan.FromSeconds(5));
        using var disabledAgain = await admin.PatchAsJsonAsync("/api/admin/clients/ecommerce", new { disabled = true }, Ct);

        Assert.Equal("Disabled", (await JsonAsync(disabledAgain)).GetProperty("status").GetString());
        Assert.Equal(disabledAt, (await host.Store.GetClientAsync("ecommerce", Ct))!.DisabledAt);

        using var refused = await product.GetAsync("/generate", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("CLIENT_DISABLED", Code(await JsonAsync(refused)));

        using var enabled = await admin.PatchAsJsonAsync("/api/admin/clients/ecommerce", new { disabled = false }, Ct);
        Assert.Equal("Active", (await JsonAsync(enabled)).GetProperty("status").GetString());
        Assert.Equal("generated", await product.GetStringAsync("/generate", Ct));

        using var renamed = await admin.PatchAsJsonAsync("/api/admin/clients/ecommerce", new { name = "Shop" }, Ct);
        Assert.Equal("Shop", (await JsonAsync(renamed)).GetProperty("name").GetString());

        using var missing = await admin.PatchAsJsonAsync("/api/admin/clients/missing", new { disabled = true }, Ct);
        Assert.Equal("CLIENT_NOT_FOUND", Code(await JsonAsync(missing)));
    }

    [Fact]
    public async Task Every_management_call_answers_503_while_storage_is_down()
    {
        await using var host = await StudHost.StartAsync();
        host.Store.Unavailable = true;

        using var response = await host.Admin().GetAsync("/api/admin/clients", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("5", response.Headers.RetryAfter?.ToString());
        Assert.Equal("STORAGE_UNAVAILABLE", Code(await JsonAsync(response)));
    }

    [Fact]
    public async Task Operator_actions_are_logged_without_the_key()
    {
        await using var host = await StudHost.StartAsync();
        await host.RegisterAsync("ecommerce", Ct);
        var issued = await host.IssueAsync("ecommerce", ["pdf:generate"], Ct);
        await host.Admin().DeleteAsync($"/api/admin/clients/ecommerce/keys/{issued.GetProperty("keyId").GetString()}", Ct);

        var text = host.Logs.AllText();
        Assert.Contains("Client ecommerce registered", text, StringComparison.Ordinal);
        Assert.Contains($"Key {issued.GetProperty("keyId").GetString()}", text, StringComparison.Ordinal);
        Assert.Contains("revoked", text, StringComparison.Ordinal);
        Assert.DoesNotContain(issued.GetProperty("key").GetString()!, text, StringComparison.Ordinal);
        Assert.DoesNotContain(StudHost.AdminKey, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_scopes_a_key_can_have_are_listed_in_declaration_order_for_the_operator_only()
    {
        await using var host = await StudHost.StartAsync();

        var scopes = await host.Admin().GetFromJsonAsync<JsonElement>("/api/admin/scopes", Ct);
        Assert.Equal(
            ["pdf:generate", "pdf:templates:read", "pdf:templates:write"],
            scopes.GetProperty("items").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.All(scopes.GetProperty("items").EnumerateArray(), s => Assert.False(string.IsNullOrEmpty(s.GetProperty("description").GetString())));

        var key = host.Store.Seed("ecommerce", ["pdf:generate", "pdf:templates:read", "pdf:templates:write"], host.Clock.Now);
        using var refused = await host.Client(key).GetAsync("/api/admin/scopes", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("AUTH_SCOPE_MISSING", Code(await JsonAsync(refused)));
    }
}
