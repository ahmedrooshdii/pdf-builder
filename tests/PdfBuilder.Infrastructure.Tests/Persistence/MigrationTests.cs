using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Infrastructure.Tests.Persistence;

public sealed class MigrationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Needs no server: it compares the model with the last migration's snapshot. It fails the build of
    /// anyone who changes a record or its configuration and forgets <c>dotnet ef migrations add</c>.
    /// </summary>
    [Fact]
    public void The_migrations_describe_the_current_model()
    {
        using var context = new PdfBuilderDbContextFactory().CreateDbContext([]);

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "The model changed without a migration. Run: dotnet tool restore, then dotnet ef migrations add <Name> " +
            "--project src/PdfBuilder.Infrastructure --startup-project src/PdfBuilder.Api --output-dir Persistence/Migrations");
    }

    [Fact]
    public async Task Migrating_an_empty_database_creates_the_schema_and_leaves_nothing_pending()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var empty = await postgres.CreateEmptyDatabaseAsync();
        await using var context = empty.CreateContext();

        await context.Database.MigrateAsync(Ct);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync(Ct));
        Assert.Equal(
            [
                "api_keys", "authored_templates", "clients", "console_sessions", "data_protection_keys",
                "inbox_state", "outbox_message", "outbox_state", "pdf_jobs", "template_imports"
            ],
            await TablesAsync(empty));
    }

    [Fact]
    public async Task Migrating_twice_changes_nothing()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var empty = await postgres.CreateEmptyDatabaseAsync();
        await using var context = empty.CreateContext();

        await context.Database.MigrateAsync(Ct);
        await context.Database.MigrateAsync(Ct);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync(Ct));
    }

    [Fact]
    public async Task A_job_cannot_be_stored_with_a_status_the_api_does_not_have()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha");   // a job names its owner, so the owner must exist

        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var connection = new NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand(
                "INSERT INTO pdf_jobs (job_id, client_id, template_key, status, enqueued_at, last_saved_at) " +
                "VALUES (gen_random_uuid(), 'alpha', 'invoice', 'Exploded', now(), now())", connection);
            await command.ExecuteNonQueryAsync(Ct);
        });

        Assert.Equal("ck_pdf_jobs_status", error.ConstraintName);
    }

    private static async Task<List<string>> TablesAsync(TestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory' ORDER BY table_name", connection);

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            tables.Add(reader.GetString(0));

        return tables;
    }

    // ---- Feature 003: the owner is guaranteed by the database -------------------------------------------

    private static async Task ExecuteAsync(TestDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private const string TemplateJson =
        """{"html":"<p>x</p>","css":"","sampleJson":"{}","options":{"format":"A4","width":null,"height":null,"landscape":false,"printBackground":true,"preferCssPageSize":true,"margins":{"top":"10mm","right":"10mm","bottom":"10mm","left":"10mm"}}}""";

    [Fact]
    public async Task The_second_migration_refuses_a_row_with_no_owner_and_leaves_the_database_as_it_was()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var empty = await postgres.CreateEmptyDatabaseAsync();
        await using var context = empty.CreateContext();
        var addOwner = context.Database.GetMigrations().Single(id => id.EndsWith("_AddClientOwner", StringComparison.Ordinal));
        await context.Database.GetService<IMigrator>().MigrateAsync(addOwner, Ct);
        await ExecuteAsync(empty,
            "INSERT INTO pdf_jobs (job_id, template_key, status, enqueued_at, last_saved_at) " +
            "VALUES (gen_random_uuid(), 'invoice', 'Pending', now(), now())");

        // A row with no owner: the service assigns one between the two migrations, and nothing else may skip that.
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.MigrateAsync(Ct));

        Assert.Equal(PostgresErrorCodes.NotNullViolation, error.SqlState);
        Assert.Single(await context.Database.GetPendingMigrationsAsync(Ct));
    }

    [Fact]
    public async Task A_template_key_is_unique_within_a_client_in_any_letter_case_but_not_across_clients()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        string Insert(string client, string key) =>
            $"INSERT INTO authored_templates (client_id, \"key\", created_at, draft, revision, updated_at) " +
            $"VALUES ('{client}', '{key}', now(), '{TemplateJson}'::jsonb, 1, now())";

        await ExecuteAsync(database, Insert("alpha", "invoice"));
        await ExecuteAsync(database, Insert("beta", "invoice"));       // the same key, another client: allowed

        var sameCase = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(database, Insert("alpha", "invoice")));
        var otherCase = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(database, Insert("alpha", "Invoice")));
        Assert.Equal("pk_authored_templates", sameCase.ConstraintName);
        Assert.Equal("ux_authored_templates_client_key_lower", otherCase.ConstraintName);
    }

    [Fact]
    public async Task A_template_or_job_cannot_name_a_client_that_is_not_registered()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();

        var template = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(database,
            $"INSERT INTO authored_templates (client_id, \"key\", created_at, draft, revision, updated_at) " +
            $"VALUES ('nobody', 'invoice', now(), '{TemplateJson}'::jsonb, 1, now())"));
        var job = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(database,
            "INSERT INTO pdf_jobs (job_id, client_id, template_key, status, enqueued_at, last_saved_at) " +
            "VALUES (gen_random_uuid(), 'nobody', 'invoice', 'Pending', now(), now())"));

        Assert.Equal("fk_authored_templates_clients_client_id", template.ConstraintName);
        Assert.Equal("fk_pdf_jobs_clients_client_id", job.ConstraintName);
    }
}
