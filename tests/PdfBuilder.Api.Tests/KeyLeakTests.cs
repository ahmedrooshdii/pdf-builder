using System.Net.Http.Json;
using System.Text;
using Npgsql;
using PdfBuilder.Api.Tests.Support;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// SC-005 and FR-008: once issued, a key appears in 0 places. Everything the service writes is searched:
/// its logs, every response after the one that issued the key, and every row of its database.
/// </summary>
public sealed class KeyLeakTests(PostgresFixture postgres)
{
    private static readonly string[] Generate = ["pdf:generate"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_issued_key_and_the_admin_key_appear_nowhere_after_the_issuing_response()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres);
        var issued = await api.IssueKeyAsync("ecommerce", Generate, Ct);
        var key = issued.GetProperty("key").GetString()!;
        var secret = key[(key.LastIndexOf('_') + 1)..];
        var keyId = issued.GetProperty("keyId").GetString();

        var written = new StringBuilder();
        async Task CallAsync(HttpClient client, HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
            using var response = await client.SendAsync(request, Ct);
            written.AppendLine(response.ToString()).AppendLine(await response.Content.ReadAsStringAsync(Ct));
        }

        using var product = api.ClientWithKey(key);
        using var admin = api.AdminClient();
        using var forged = api.ClientWithKey(key[..^1] + (key[^1] == 'a' ? 'b' : 'a'));

        await CallAsync(product, HttpMethod.Post, "/api/pdf/jobs", new { templateKey = "invoice", data = new { } });
        await CallAsync(product, HttpMethod.Get, "/api/templates");                         // refused: no permission
        await CallAsync(forged, HttpMethod.Post, "/api/pdf/jobs", new { templateKey = "invoice", data = new { } });
        await CallAsync(admin, HttpMethod.Get, "/api/admin/clients/ecommerce/keys");
        await CallAsync(admin, HttpMethod.Delete, $"/api/admin/clients/ecommerce/keys/{keyId}");
        await CallAsync(product, HttpMethod.Post, "/api/pdf/jobs", new { templateKey = "invoice", data = new { } });   // revoked
        await Task.Delay(KeyUsageDelay, Ct);

        await using var connection = new NpgsqlConnection(api.Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var tables = new NpgsqlCommand(
            "SELECT string_agg(table_name, ',') FROM information_schema.tables WHERE table_schema = 'public'", connection);
        foreach (var table in ((string)(await tables.ExecuteScalarAsync(Ct))!).Split(','))
        {
            await using var dump = new NpgsqlCommand(
                $"SELECT coalesce(string_agg(row_to_json(t)::text, ''), '') FROM \"{table}\" t", connection);
            written.AppendLine((string)(await dump.ExecuteScalarAsync(Ct))!);
        }

        written.AppendLine(api.Logs.AllText());
        var everything = written.ToString();

        Assert.DoesNotContain(secret, everything, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiFactory.AdminKey[4..], everything, StringComparison.Ordinal);
        Assert.Contains(keyId!, everything, StringComparison.Ordinal);     // the public id is fine, and proves the search saw the key's rows
    }

    /// <summary>Long enough for the last-use recorder to have written, so its write is searched too.</summary>
    private static readonly TimeSpan KeyUsageDelay = TimeSpan.FromSeconds(4);
}
