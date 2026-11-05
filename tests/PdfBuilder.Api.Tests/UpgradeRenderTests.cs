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
/// Feature 003, SC-005 on the whole service: a deployment from before clients, with templates and a job in it,
/// is started with one setting, and the client it names renders what it rendered before.
/// </summary>
public sealed class UpgradeRenderTests(PostgresFixture postgres)
{
    private const string OldJobId = "11111111-1111-1111-1111-111111111111";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Template content as the 1.0 service stored it.</summary>
    private static string Content(string html, string css) => JsonSerializer.Serialize(new
    {
        html,
        css,
        sampleJson = "{}",
        options = new
        {
            format = "A4",
            width = (string?)null,
            height = (string?)null,
            landscape = false,
            printBackground = true,
            preferCssPageSize = true,
            margins = new { top = "10mm", right = "10mm", bottom = "10mm", left = "10mm" }
        }
    });

    /// <summary>A database at the last schema that had no owner column, with what a 1.0 deployment would hold.</summary>
    private async Task<TestDatabase> SeedBeforeClientsAsync()
    {
        var database = await postgres.CreateEmptyDatabaseAsync();
        await using (var context = database.CreateContext())
        {
            var before = context.Database.GetMigrations().Single(id => id.EndsWith("_AddConsoleSessions", StringComparison.Ordinal));
            await context.Database.GetService<IMigrator>().MigrateAsync(before, Ct);
        }

        var letter = Content("<p>LETTER-FROM-BEFORE-CLIENTS</p>", "p { color: navy; }");
        var receipt = Content("<p>RECEIPT-DRAFT-ONLY</p>", "");

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand($"""
            INSERT INTO authored_templates ("key", created_at, draft, published, published_at, revision, updated_at)
            VALUES ('letter', now(), '{letter}'::jsonb, '{letter}'::jsonb, now(), 2, now()),
                   ('receipt', now(), '{receipt}'::jsonb, NULL, NULL, 1, now());
            INSERT INTO pdf_jobs (job_id, template_key, status, enqueued_at, last_saved_at)
            VALUES ('{OldJobId}', 'letter', 'Pending', now(), now());
            """, connection);
        await command.ExecuteNonQueryAsync(Ct);
        return database;
    }

    [Fact]
    public async Task After_the_upgrade_the_named_client_has_its_templates_and_job_unchanged_and_renders_them_and_nobody_else_sees_them()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await SeedBeforeClientsAsync();
        var renderer = new RecordingRenderer();

        await using var api = ApiFactory.StartOn(
            database,
            settings: new Dictionary<string, string?> { ["Blocks:Bus:DefaultClientId"] = "legacy" },
            configure: services => services.AddSingleton<IPdfRenderer>(renderer));
        using var legacy = await api.ClientForAsync("legacy", Ct);
        using var other = await api.ClientForAsync("other", Ct);

        // The client was registered by the upgrade, named by its id.
        using var clients = await api.AdminClient().GetAsync("/api/admin/clients/legacy", Ct);
        Assert.Equal("legacy", (await clients.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("name").GetString());

        // Its templates, as they were: drafts, published versions and revisions.
        var letter = await legacy.GetFromJsonAsync<JsonElement>("/api/templates/letter", Ct);
        Assert.Equal("Published", letter.GetProperty("status").GetString());
        Assert.Equal(2, letter.GetProperty("revision").GetInt32());
        Assert.Equal("<p>LETTER-FROM-BEFORE-CLIENTS</p>", letter.GetProperty("published").GetProperty("html").GetString());
        Assert.Equal("p { color: navy; }", letter.GetProperty("published").GetProperty("css").GetString());

        var receipt = await legacy.GetFromJsonAsync<JsonElement>("/api/templates/receipt", Ct);
        Assert.Equal(1, receipt.GetProperty("revision").GetInt32());
        Assert.Equal(JsonValueKind.Null, receipt.GetProperty("published").ValueKind);

        // It renders what it rendered before.
        var jobId = await ApiFactory.SubmitJobAsync(legacy, "letter", new { }, Ct);
        Assert.Equal("Succeeded", (await ApiFactory.WaitForJobAsync(legacy, jobId, Ct)).GetProperty("status").GetString());
        Assert.Single(renderer.Html, html => html.Contains("LETTER-FROM-BEFORE-CLIENTS", StringComparison.Ordinal));

        // The job that was there is its job, and nobody else's.
        var oldJob = await legacy.GetFromJsonAsync<JsonElement>($"/api/pdf/jobs/{OldJobId}", Ct);
        Assert.Equal("letter", oldJob.GetProperty("templateKey").GetString());
        using var hidden = await other.GetAsync($"/api/pdf/jobs/{OldJobId}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        // Another client sees none of it.
        using var notThere = await other.GetAsync("/api/templates/letter", Ct);
        Assert.Equal(HttpStatusCode.NotFound, notThere.StatusCode);
        var list = await other.GetFromJsonAsync<JsonElement>("/api/templates", Ct);
        Assert.DoesNotContain("letter", list.EnumerateArray().Select(t => t.GetProperty("templateKey").GetString()));
    }

    [Fact]
    public async Task Without_the_setting_the_service_does_not_start_and_says_which_one_is_missing()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await SeedBeforeClientsAsync();

        var error = Assert.ThrowsAny<Exception>(() => ApiFactory.StartOn(database));

        var message = string.Join(" ", Flatten(error));
        Assert.Contains("Blocks:Bus:DefaultClientId", message, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Flatten(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            yield return current.Message;
    }
}
