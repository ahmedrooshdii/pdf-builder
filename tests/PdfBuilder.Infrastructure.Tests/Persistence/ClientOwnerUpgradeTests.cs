using Blocks.ServiceDefaults;
using Blocks.ServiceDefaults.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Infrastructure.Tests.Persistence;

/// <summary>
/// Feature 003, User Story 4: a database from before clients existed, with templates and jobs in it, is
/// upgraded by naming one client, and only then. Runs the real start-up sequence against a database built at
/// the previous schema and filled with rows.
/// </summary>
public sealed class ClientOwnerUpgradeTests(PostgresFixture postgres) : IDisposable
{
    private const string Content =
        """{"html":"<p>from before clients</p>","css":"p { color: red; }","sampleJson":"{}","options":{"format":"A4","width":null,"height":null,"landscape":false,"printBackground":true,"preferCssPageSize":true,"margins":{"top":"10mm","right":"10mm","bottom":"10mm","left":"10mm"}}}""";

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"pdf-builder-upgrade-{Guid.NewGuid():N}");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
            Directory.Delete(_scratch, recursive: true);
    }

    /// <summary>A database at the last schema that had no owner column, holding two templates and a job.</summary>
    private async Task<TestDatabase> SeedBeforeClientsAsync(bool withRows = true)
    {
        PostgresFixture.SkipUnlessEnabled();
        var database = await postgres.CreateEmptyDatabaseAsync();

        await using (var context = database.CreateContext())
        {
            var before = context.Database.GetMigrations().Single(id => id.EndsWith("_AddConsoleSessions", StringComparison.Ordinal));
            await context.Database.GetService<IMigrator>().MigrateAsync(before, Ct);
        }

        if (!withRows)
            return database;

        await ExecuteAsync(database, $"""
            INSERT INTO authored_templates ("key", created_at, draft, published, published_at, revision, updated_at)
            VALUES ('letter', '2026-10-28T12:00:00Z', '{Content}'::jsonb, '{Content}'::jsonb, '2026-10-28T12:01:00Z', 2, '2026-10-28T12:01:00Z'),
                   ('receipt', '2026-10-28T12:00:00Z', '{Content}'::jsonb, NULL, NULL, 1, '2026-10-28T12:00:00Z');
            INSERT INTO pdf_jobs (job_id, template_key, status, enqueued_at, last_saved_at)
            VALUES ('11111111-1111-1111-1111-111111111111', 'letter', 'Pending', now(), now());
            """);
        return database;
    }

    private ServiceProvider Instance(TestDatabase database, string? defaultClientId, string name = "a")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = database.ConnectionString,
                ["TemplateStore:RootPath"] = Path.Combine(_scratch, name, "built-in"),
                ["TemplateStore:AuthoredRootPath"] = Path.Combine(_scratch, name, "authored"),   // nothing to import
                ["Storage:RootPath"] = Path.Combine(_scratch, name, "files")
            })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddSingleton(TimeProvider.System)
            .AddSingleton(Options.Create(new BlocksBusOptions { DefaultClientId = defaultClientId }))
            .AddScoped<ICurrentClient>(_ => new FixedCurrentClient())
            .AddInfrastructure(configuration)
            .BuildServiceProvider(validateScopes: true);
    }

    private static Task StartAsync(ServiceProvider instance) =>
        instance.GetServices<IHostedService>().OfType<DatabaseStartup>().Single().StartAsync(Ct);

    private static async Task ExecuteAsync(TestDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>Every row of the query, columns joined, for comparing before and after.</summary>
    private static async Task<List<string>> RowsAsync(TestDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);

        var rows = new List<string>();
        while (await reader.ReadAsync(Ct))
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "∅" : reader.GetValue(i).ToString())));

        return rows;
    }

    private const string TemplateContentQuery = "SELECT \"key\", draft::text, published::text, revision, updated_at FROM authored_templates ORDER BY \"key\"";
    private const string JobQuery = "SELECT job_id, template_key, status FROM pdf_jobs ORDER BY job_id";

    private static async Task<List<string>> OwnersAsync(TestDatabase database) =>
        await RowsAsync(database,
            "SELECT 'template', \"key\", client_id FROM authored_templates UNION ALL SELECT 'job', job_id::text, client_id FROM pdf_jobs ORDER BY 1, 2");

    private static async Task<List<string>> AppliedMigrationsAsync(TestDatabase database) =>
        await RowsAsync(database, "SELECT migration_id FROM \"__EFMigrationsHistory\" ORDER BY migration_id");

    [Fact]
    public async Task Without_a_default_client_it_refuses_to_start_says_which_setting_to_fill_in_and_changes_no_row()
    {
        await using var database = await SeedBeforeClientsAsync();
        var templatesBefore = await RowsAsync(database, TemplateContentQuery);
        var jobsBefore = await RowsAsync(database, JobQuery);
        await using var instance = Instance(database, defaultClientId: null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(instance));

        Assert.Equal(ClientOwnerAssignment.MissingSetting, error.Message);
        Assert.Contains("Blocks:Bus:DefaultClientId", error.Message, StringComparison.Ordinal);
        Assert.Contains("Blocks__Bus__DefaultClientId", error.Message, StringComparison.Ordinal);

        // Nothing of the data moved, nobody owns anything, and the second half of the upgrade did not run.
        Assert.Equal(templatesBefore, await RowsAsync(database, TemplateContentQuery));
        Assert.Equal(jobsBefore, await RowsAsync(database, JobQuery));
        Assert.All(await OwnersAsync(database), row => Assert.EndsWith("|∅", row, StringComparison.Ordinal));
        Assert.DoesNotContain(await AppliedMigrationsAsync(database), id => id.EndsWith("_RequireClientOwner", StringComparison.Ordinal));
        Assert.Empty(await RowsAsync(database, "SELECT id FROM clients"));

        // Then the operator does what the message says, and the same database starts.
        await using var fixedInstance = Instance(database, defaultClientId: "legacy", name: "b");
        await StartAsync(fixedInstance);
        Assert.All(await OwnersAsync(database), row => Assert.EndsWith("|legacy", row, StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_a_default_client_that_is_not_registered_it_registers_it_and_gives_it_everything_unchanged()
    {
        await using var database = await SeedBeforeClientsAsync();
        var templatesBefore = await RowsAsync(database, TemplateContentQuery);
        var jobsBefore = await RowsAsync(database, JobQuery);
        await using var instance = Instance(database, defaultClientId: "legacy");

        await StartAsync(instance);

        // The client exists, named by its id, and is enabled.
        var client = Assert.Single(await RowsAsync(database, "SELECT id, name, disabled_at FROM clients"));
        Assert.Equal("legacy|legacy|∅", client);

        // Every template and job is its, and nothing about them changed: content, revisions, timestamps.
        Assert.Equal(
            ["job|11111111-1111-1111-1111-111111111111|legacy", "template|letter|legacy", "template|receipt|legacy"],
            await OwnersAsync(database));
        Assert.Equal(templatesBefore, await RowsAsync(database, TemplateContentQuery));
        Assert.Equal(jobsBefore, await RowsAsync(database, JobQuery));

        // The schema is complete: an owner is now required.
        Assert.Contains(await AppliedMigrationsAsync(database), id => id.EndsWith("_RequireClientOwner", StringComparison.Ordinal));
        await using var context = database.CreateContext();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(Ct));
    }

    [Fact]
    public async Task A_default_client_that_is_already_registered_is_used_as_it_is_even_when_disabled()
    {
        await using var database = await SeedBeforeClientsAsync();
        await ExecuteAsync(database, "INSERT INTO clients (id, name, created_at, disabled_at) VALUES ('legacy', 'Legacy Co', now(), now())");
        await using var instance = Instance(database, defaultClientId: "legacy");

        await StartAsync(instance);

        var client = Assert.Single(await RowsAsync(database, "SELECT id, name, disabled_at IS NOT NULL FROM clients"));
        Assert.Equal("legacy|Legacy Co|True", client);
        Assert.All(await OwnersAsync(database), row => Assert.EndsWith("|legacy", row, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_instances_starting_together_give_everything_one_owner_and_register_it_once()
    {
        await using var database = await SeedBeforeClientsAsync();
        await using var first = Instance(database, defaultClientId: "legacy", name: "a");
        await using var second = Instance(database, defaultClientId: "legacy", name: "b");

        await Task.WhenAll(StartAsync(first), StartAsync(second));

        Assert.Single(await RowsAsync(database, "SELECT id FROM clients"));
        Assert.Equal(
            ["job|11111111-1111-1111-1111-111111111111|legacy", "template|letter|legacy", "template|receipt|legacy"],
            await OwnersAsync(database));
        Assert.Single(await AppliedMigrationsAsync(database), id => id.EndsWith("_RequireClientOwner", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Once_upgraded_a_later_start_or_a_changed_setting_moves_nothing()
    {
        await using var database = await SeedBeforeClientsAsync();
        await using (var upgrade = Instance(database, defaultClientId: "legacy"))
            await StartAsync(upgrade);
        var owners = await OwnersAsync(database);

        await using var restart = Instance(database, defaultClientId: "other", name: "b");
        await StartAsync(restart);
        await using var noSetting = Instance(database, defaultClientId: null, name: "c");
        await StartAsync(noSetting);

        Assert.Equal(owners, await OwnersAsync(database));
        Assert.Equal(["legacy"], await RowsAsync(database, "SELECT id FROM clients"));
    }

    [Fact]
    public async Task A_database_with_no_templates_or_jobs_from_before_starts_without_a_setting()
    {
        // Nothing needs an owner, so nothing asks for one: a fresh install, or an upgrade of an empty one.
        PostgresFixture.SkipUnlessEnabled();
        await using var fresh = await postgres.CreateEmptyDatabaseAsync();
        await using var instance = Instance(fresh, defaultClientId: null);

        await StartAsync(instance);

        await using var context = fresh.CreateContext();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(Ct));
        Assert.Empty(await RowsAsync(fresh, "SELECT id FROM clients"));

        await using var emptyOld = await SeedBeforeClientsAsync(withRows: false);
        await using var oldInstance = Instance(emptyOld, defaultClientId: null, name: "b");
        await StartAsync(oldInstance);
        Assert.Contains(await AppliedMigrationsAsync(emptyOld), id => id.EndsWith("_RequireClientOwner", StringComparison.Ordinal));
    }
}
