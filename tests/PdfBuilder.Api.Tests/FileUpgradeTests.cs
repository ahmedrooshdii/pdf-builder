using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PdfBuilder.Api.Tests.Support;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// Feature 005, US5: the upgrade needs one setting and does nothing without it, links behave as before while the
/// switch is off, and the switch can be turned on and off again without breaking a link that was handed out.
/// </summary>
public sealed class FileUpgradeTests(PostgresFixture postgres) : IDisposable
{
    private readonly List<string> _roots = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string NewStorageRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pdf-builder-upgrade-{Guid.NewGuid():N}");
        _roots.Add(root);
        return root;
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
            Directory.Delete(root, recursive: true);
    }

    private static Dictionary<string, string?> Settings(bool requireSignature, string? storageRoot = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Blocks:Files:RequireSignature"] = requireSignature ? "true" : "false",
            ["Blocks:Files:UrlLifetime"] = "00:10:00"
        };
        if (storageRoot is not null)
            settings["Storage:RootPath"] = storageRoot;
        return settings;
    }

    private static ApiFactory Start(TestDatabase database, TestClock clock, bool requireSignature, string? storageRoot = null) =>
        ApiFactory.StartOn(
            database,
            settings: Settings(requireSignature, storageRoot),
            configure: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton<IPdfRenderer>(new RecordingRenderer());
            });

    private static IEnumerable<string> Flatten(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            yield return current.Message;
    }

    [Fact]
    public async Task Without_the_signing_key_the_service_does_not_start_and_changes_nothing_in_the_database()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateEmptyDatabaseAsync();
        await using (var context = database.CreateContext())
        {
            // The database as the previous version left it.
            var before = context.Database.GetMigrations().Single(id => id.EndsWith("_RequireClientOwner", StringComparison.Ordinal));
            await context.Database.GetService<IMigrator>().MigrateAsync(before, Ct);
        }

        var error = Assert.ThrowsAny<Exception>(() => ApiFactory.StartOn(
            database,
            settings: new Dictionary<string, string?> { ["Blocks:Files:SigningKey"] = "", ["Blocks:Bus:DefaultClientId"] = "legacy" }));

        var message = string.Join(" ", Flatten(error));
        Assert.Contains("Blocks:Files:SigningKey", message, StringComparison.Ordinal);
        Assert.Contains("Blocks__Files__SigningKey", message, StringComparison.Ordinal);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var history = new NpgsqlCommand("""SELECT count(*) FROM "__EFMigrationsHistory" WHERE migration_id LIKE '%AddFileLifetime'""", connection);
        Assert.Equal(0L, await history.ExecuteScalarAsync(Ct));
        await using var column = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'pdf_jobs' AND column_name = 'file_url'", connection);
        Assert.Equal(1L, await column.ExecuteScalarAsync(Ct));      // the schema is as it was
    }

    [Fact]
    public async Task With_the_switch_off_links_are_the_plain_ones_and_have_no_expiry()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        await using var api = Start(database, clock, requireSignature: false);
        using var shop = await api.ClientForAsync("shop", Ct);

        var id = await ApiFactory.SubmitJobAsync(shop, "invoice", new { }, Ct);
        var job = await ApiFactory.WaitForJobAsync(shop, id, Ct);

        var url = job.GetProperty("fileUrl").GetString()!;
        Assert.EndsWith($"/files/{job.GetProperty("fileId").GetString()}", url, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, job.GetProperty("fileUrlExpiresAt").ValueKind);
        using var anonymous = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(new Uri(url).PathAndQuery, Ct)).StatusCode);
    }

    [Fact]
    public async Task Turning_the_switch_on_refuses_old_unsigned_links_and_turning_it_off_keeps_signed_ones_working_until_they_expire()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        var clock = new TestClock(DateTimeOffset.UtcNow);

        // The three instances are one deployment: they read the same files.
        var storage = NewStorageRoot();

        string unsignedLink;
        string signedLink;
        await using (var off = Start(database, clock, requireSignature: false, storage))
        {
            using var shop = await off.ClientForAsync("shop", Ct);
            var id = await ApiFactory.SubmitJobAsync(shop, "invoice", new { }, Ct);
            var job = await ApiFactory.WaitForJobAsync(shop, id, Ct);
            unsignedLink = new Uri(job.GetProperty("fileUrl").GetString()!).PathAndQuery;
            signedLink = "";
        }

        await using (var on = Start(database, clock, requireSignature: true, storage))
        {
            using var anonymous = on.CreateClient();
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(unsignedLink, Ct)).StatusCode);

            // New jobs get signed links, and a fresh link can be asked for an old job.
            using var shop = await on.ClientForAsync("shop", Ct);
            var id = await ApiFactory.SubmitJobAsync(shop, "invoice", new { }, Ct);
            var job = await ApiFactory.WaitForJobAsync(shop, id, Ct);
            signedLink = new Uri(job.GetProperty("fileUrl").GetString()!).PathAndQuery;
            Assert.Contains("sig=", signedLink, StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(signedLink, Ct)).StatusCode);
        }

        // Back to off: the unsigned link works again, and the signed link keeps working until it expires.
        await using (var off = Start(database, clock, requireSignature: false, storage))
        {
            using var anonymous = off.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(unsignedLink, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(signedLink, Ct)).StatusCode);

            clock.Now += TimeSpan.FromMinutes(11);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(signedLink, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(unsignedLink, Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task A_job_from_before_the_upgrade_gets_a_link_made_from_its_file_id()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateEmptyDatabaseAsync();
        await using (var context = database.CreateContext())
        {
            var before = context.Database.GetMigrations().Single(id => id.EndsWith("_RequireClientOwner", StringComparison.Ordinal));
            await context.Database.GetService<IMigrator>().MigrateAsync(before, Ct);
        }

        var jobId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var seed = new NpgsqlCommand($"""
                INSERT INTO clients (id, name, created_at) VALUES ('shop', 'shop', now());
                INSERT INTO pdf_jobs (job_id, client_id, template_key, status, enqueued_at, completed_at, file_id, file_url, size_bytes, last_saved_at)
                VALUES ('{jobId}', 'shop', 'invoice', 'Succeeded', now(), now(), 'a7e0495b2cd24ed9bb65b1b0f3a14c20',
                        'http://old.example/files/a7e0495b2cd24ed9bb65b1b0f3a14c20', 10, now());
                """, connection);
            await seed.ExecuteNonQueryAsync(Ct);
        }

        await using var api = ApiFactory.StartOn(
            database,
            settings: new Dictionary<string, string?> { ["Storage:PublicBaseUrl"] = "https://pdf.example.com" });
        // The file the old version wrote, where it wrote it.
        var root = ((PdfBuilder.Infrastructure.Storage.LocalFileStorage)api.Services.GetRequiredService<IFileStorage>()).RootPath;
        Directory.CreateDirectory(Path.Combine(root, "a7"));
        await File.WriteAllBytesAsync(Path.Combine(root, "a7", "a7e0495b2cd24ed9bb65b1b0f3a14c20.pdf"), [0x25, 0x50, 0x44, 0x46], Ct);
        using var shop = await api.ClientForAsync("shop", Ct);

        var job = await shop.GetFromJsonAsync<JsonElement>($"/api/pdf/jobs/{jobId}", Ct);

        // The old stored link is gone with its column; links come from the file id and today's public address.
        Assert.Equal("https://pdf.example.com/files/a7e0495b2cd24ed9bb65b1b0f3a14c20", job.GetProperty("fileUrl").GetString());
        Assert.False(job.GetProperty("fileExpired").GetBoolean());
        using var anonymous = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/files/a7e0495b2cd24ed9bb65b1b0f3a14c20", Ct)).StatusCode);
    }
}
