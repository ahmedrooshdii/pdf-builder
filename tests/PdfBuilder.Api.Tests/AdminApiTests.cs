using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using PdfBuilder.Api.Tests.Support;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// The management API on the real service and database: Story 1 (register, issue once), Story 4
/// (revoke, disable, expire) and Story 6 (last use).
/// </summary>
public sealed class AdminApiTests(PostgresFixture postgres)
{
    private static readonly string[] Generate = ["pdf:generate"];
    private static readonly string[] UnknownScope = ["pdf:everything"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static object Job() => new { templateKey = "invoice", data = new { } };

    [Fact]
    public async Task The_operator_registers_a_client_and_its_key_works_at_once_and_is_never_shown_again()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var admin = api.AdminClient();

        using var created = await admin.PostAsJsonAsync("/api/admin/clients", new { id = "ecommerce", name = "E-commerce" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/admin/clients/ecommerce", created.Headers.Location?.OriginalString);
        Assert.Equal("Active", (await JsonAsync(created)).GetProperty("status").GetString());

        using var issued = await admin.PostAsJsonAsync(
            "/api/admin/clients/ecommerce/keys", new { name = "production", scopes = Generate }, Ct);
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        Assert.True(issued.Headers.CacheControl?.NoStore);
        var body = await JsonAsync(issued);
        var key = body.GetProperty("key").GetString()!;
        var secret = key[(key.LastIndexOf('_') + 1)..];
        Assert.StartsWith($"pdf_{body.GetProperty("keyId").GetString()}_", key, StringComparison.Ordinal);

        using var product = api.ClientWithKey(key);
        using var job = await product.PostAsJsonAsync("/api/pdf/jobs", Job(), Ct);
        Assert.Equal(HttpStatusCode.Accepted, job.StatusCode);

        var listed = await admin.GetStringAsync("/api/admin/clients/ecommerce/keys", Ct);
        Assert.DoesNotContain(secret, listed, StringComparison.Ordinal);

        var clients = await admin.GetFromJsonAsync<JsonElement>("/api/admin/clients", Ct);
        Assert.Equal(["ecommerce"], clients.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetString()));

        // SC-005: nothing stored holds the secret.
        await using var connection = new NpgsqlConnection(api.Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var dump = new NpgsqlCommand("SELECT coalesce(string_agg(row_to_json(api_keys)::text, ''), '') FROM api_keys", connection);
        Assert.DoesNotContain(secret, (string)(await dump.ExecuteScalarAsync(Ct))!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rules_and_unknown_resources_are_refused_with_their_codes()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var admin = api.AdminClient();
        await api.IssueKeyAsync("ecommerce", Generate, Ct);

        using var taken = await admin.PostAsJsonAsync("/api/admin/clients", new { id = "ecommerce", name = "Again" }, Ct);
        using var invalid = await admin.PostAsJsonAsync("/api/admin/clients", new { id = "E-Commerce", name = "E" }, Ct);
        using var missing = await admin.GetAsync("/api/admin/clients/missing", Ct);
        using var unknownScope = await admin.PostAsJsonAsync(
            "/api/admin/clients/ecommerce/keys", new { name = "x", scopes = UnknownScope }, Ct);

        Assert.Equal("CLIENT_ID_TAKEN", Code(await JsonAsync(taken)));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("VALIDATION_FAILED", Code(await JsonAsync(invalid)));
        Assert.Equal("CLIENT_NOT_FOUND", Code(await JsonAsync(missing)));
        Assert.Contains("pdf:templates:write", (await JsonAsync(unknownScope)).GetProperty("errors").GetProperty("scopes")[0].GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_revoked_key_is_refused_while_the_clients_other_key_keeps_working()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var admin = api.AdminClient();
        var revoked = await api.IssueKeyAsync("ecommerce", Generate, Ct);
        var other = await api.IssueKeyAsync("ecommerce", Generate, Ct);
        var keyId = revoked.GetProperty("keyId").GetString();

        using var product = api.ClientWithKey(revoked.GetProperty("key").GetString());
        Assert.Equal(HttpStatusCode.Accepted, (await product.PostAsJsonAsync("/api/pdf/jobs", Job(), Ct)).StatusCode);

        using var first = await admin.DeleteAsync($"/api/admin/clients/ecommerce/keys/{keyId}", Ct);
        using var again = await admin.DeleteAsync($"/api/admin/clients/ecommerce/keys/{keyId}", Ct);
        using var unknown = await admin.DeleteAsync("/api/admin/clients/ecommerce/keys/zzzzzzzz", Ct);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal("KEY_NOT_FOUND", Code(await JsonAsync(unknown)));

        using var refused = await product.PostAsJsonAsync("/api/pdf/jobs", Job(), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal("AUTH_INVALID_KEY", Code(await JsonAsync(refused)));

        using var stillWorks = api.ClientWithKey(other.GetProperty("key").GetString());
        Assert.Equal(HttpStatusCode.Accepted, (await stillWorks.PostAsJsonAsync("/api/pdf/jobs", Job(), Ct)).StatusCode);
    }

    [Fact]
    public async Task Disabling_a_client_refuses_its_keys_until_it_is_enabled_again()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var admin = api.AdminClient();
        using var product = api.ClientWithKey((await api.IssueKeyAsync("ecommerce", Generate, Ct)).GetProperty("key").GetString());

        using var disabled = await admin.PatchAsJsonAsync("/api/admin/clients/ecommerce", new { disabled = true }, Ct);
        Assert.Equal("Disabled", (await JsonAsync(disabled)).GetProperty("status").GetString());

        using var refused = await product.PostAsJsonAsync("/api/pdf/jobs", Job(), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("CLIENT_DISABLED", Code(await JsonAsync(refused)));

        await admin.PatchAsJsonAsync("/api/admin/clients/ecommerce", new { disabled = false }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, (await product.PostAsJsonAsync("/api/pdf/jobs", Job(), Ct)).StatusCode);
    }

    [Fact]
    public async Task An_expired_key_is_refused_and_listed_as_expired()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var admin = api.AdminClient();

        using var past = await admin.PostAsJsonAsync(
            "/api/admin/clients/ecommerce/keys",
            new { name = "x", scopes = Generate, expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }, Ct);
        await api.IssueKeyAsync("ecommerce", Generate, Ct);
        using var pastAfterRegistering = await admin.PostAsJsonAsync(
            "/api/admin/clients/ecommerce/keys",
            new { name = "x", scopes = Generate, expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, past.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, pastAfterRegistering.StatusCode);

        var shortLived = await api.IssueKeyAsync("ecommerce", Generate, Ct, expiresAt: DateTimeOffset.UtcNow.AddSeconds(2));
        await Task.Delay(TimeSpan.FromSeconds(3), Ct);

        using var product = api.ClientWithKey(shortLived.GetProperty("key").GetString());
        using var refused = await product.PostAsJsonAsync("/api/pdf/jobs", Job(), Ct);
        Assert.Equal("AUTH_INVALID_KEY", Code(await JsonAsync(refused)));

        var keys = await admin.GetFromJsonAsync<JsonElement>("/api/admin/clients/ecommerce/keys", Ct);
        Assert.Equal("Expired", keys.GetProperty("items").EnumerateArray()
            .Single(k => k.GetProperty("keyId").GetString() == shortLived.GetProperty("keyId").GetString())
            .GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_used_key_shows_when_it_was_last_used_and_an_unused_one_shows_null()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var admin = api.AdminClient();
        var used = await api.IssueKeyAsync("ecommerce", Generate, Ct);
        var unused = await api.IssueKeyAsync("ecommerce", Generate, Ct);

        var calledAt = DateTimeOffset.UtcNow;
        using var product = api.ClientWithKey(used.GetProperty("key").GetString());
        await product.GetAsync($"/api/pdf/jobs/{Guid.NewGuid()}", Ct);

        // The recorder writes in the background every few seconds.
        JsonElement usedKey = default;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var keys = await admin.GetFromJsonAsync<JsonElement>("/api/admin/clients/ecommerce/keys", Ct);
            usedKey = keys.GetProperty("items").EnumerateArray().Single(k => k.GetProperty("keyId").GetString() == used.GetProperty("keyId").GetString());
            if (usedKey.GetProperty("lastUsedAt").ValueKind != JsonValueKind.Null)
            {
                var unusedKey = keys.GetProperty("items").EnumerateArray().Single(k => k.GetProperty("keyId").GetString() == unused.GetProperty("keyId").GetString());
                Assert.Equal(JsonValueKind.Null, unusedKey.GetProperty("lastUsedAt").ValueKind);
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);
        }

        var lastUsedAt = usedKey.GetProperty("lastUsedAt").GetDateTimeOffset();
        Assert.InRange(lastUsedAt, calledAt.AddMinutes(-1), calledAt.AddMinutes(1));
    }
}
