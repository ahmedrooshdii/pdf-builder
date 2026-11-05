using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using PdfBuilder.Api.Tests.Support;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// Story 1 on the real service and PostgreSQL: a session works on every endpoint the admin key does,
/// on every instance and across restarts (FR-004, FR-010); sign-out on one instance ends it on another
/// (FR-005); a new admin key ends it (FR-006). The cookie's key ring lives in <c>data_protection_keys</c>.
/// </summary>
public sealed class ConsoleSessionApiTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_session_reaches_admin_job_and_template_endpoints_like_the_admin_key()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        var cookie = await api.SignInAsync(Ct);
        await api.EnsureClientAsync("ecommerce", Ct);

        // The operator works inside a client: templates and jobs belong to one (feature 003).
        using var session = api.SessionClient(cookie, actingAs: "ecommerce");

        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/admin/clients", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/templates", Ct)).StatusCode);

        using var job = await session.PostAsJsonAsync("/api/pdf/jobs", new { templateKey = "invoice", data = new { } }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, job.StatusCode);
    }

    [Fact]
    public async Task One_session_works_on_two_instances_and_signing_out_on_one_ends_it_on_the_other()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var a = await ApiFactory.StartAsync(postgres);
        await using var b = ApiFactory.StartOn(a.Database);
        var cookie = await a.SignInAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, (await b.SessionClient(cookie).GetAsync("/api/console/session", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await a.SessionClient(cookie).DeleteAsync("/api/console/session", Ct)).StatusCode);

        using var refused = await b.SessionClient(cookie).GetAsync("/api/admin/clients", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal("AUTH_REQUIRED", (await refused.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_session_survives_a_restart_and_ends_when_the_admin_key_is_replaced()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var first = await ApiFactory.StartAsync(postgres);
        var cookie = await first.SignInAsync(Ct);

        await using (var restarted = ApiFactory.StartOn(first.Database))
            Assert.Equal(HttpStatusCode.OK, (await restarted.SessionClient(cookie).GetAsync("/api/console/session", Ct)).StatusCode);

        var newHash = Blocks.ServiceDefaults.Authentication.KeyHasher.HashAdminKey(
            Blocks.ServiceDefaults.Authentication.ApiKeyFormat.NewAdminKey());
        await using var rotated = ApiFactory.StartOn(first.Database, newHash);
        Assert.Equal(HttpStatusCode.Unauthorized, (await rotated.SessionClient(cookie).GetAsync("/api/console/session", Ct)).StatusCode);
    }

    [Fact]
    public async Task Sessions_and_their_key_ring_are_stored_without_the_session_id()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        var cookie = await api.SignInAsync(Ct);

        await using var connection = new NpgsqlConnection(api.Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var sessions = new NpgsqlCommand(
            "SELECT count(*) FROM console_sessions WHERE revoked_at IS NULL AND expires_at > now()", connection);
        Assert.Equal(1L, await sessions.ExecuteScalarAsync(Ct));
        await using var keys = new NpgsqlCommand("SELECT count(*) FROM data_protection_keys", connection);
        Assert.True((long)(await keys.ExecuteScalarAsync(Ct))! >= 1);

        // The cookie value never appears in the database: only a hash of the id it carries.
        await using var dump = new NpgsqlCommand(
            "SELECT encode(id_hash, 'base64') || encode(admin_key_fingerprint, 'base64') FROM console_sessions", connection);
        var stored = (string)(await dump.ExecuteScalarAsync(Ct))!;
        Assert.DoesNotContain(cookie.Split('=', 2)[1], stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_scopes_endpoint_lists_the_pdf_permissions_with_their_descriptions()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);

        var scopes = await api.AdminClient().GetFromJsonAsync<JsonElement>("/api/admin/scopes", Ct);

        Assert.Equal(
            [
                ("pdf:generate", "Submit jobs and read them."),
                ("pdf:templates:read", "List and read templates."),
                ("pdf:templates:write", "Create, duplicate, edit, publish, delete and preview templates.")
            ],
            scopes.GetProperty("items").EnumerateArray()
                .Select(s => (s.GetProperty("name").GetString(), s.GetProperty("description").GetString())));
    }

    [Fact]
    public async Task An_admin_acting_as_a_client_is_logged_with_that_client()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        using var admin = await api.AdminActingAsAsync("ecommerce", Ct);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/templates", Ct)).StatusCode);

        Assert.Contains(api.Logs.Entries, e =>
            e.Scope.TryGetValue("Admin", out var isAdmin) && Equals(isAdmin, true)
            && e.Scope.TryGetValue("ClientId", out var client) && Equals(client, "ecommerce"));
    }
}
