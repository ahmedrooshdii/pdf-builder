using Microsoft.EntityFrameworkCore;
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
            ["authored_templates", "inbox_state", "outbox_message", "outbox_state", "pdf_jobs", "template_imports"],
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

        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var connection = new NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand(
                "INSERT INTO pdf_jobs (job_id, template_key, status, enqueued_at, last_saved_at) " +
                "VALUES (gen_random_uuid(), 'invoice', 'Exploded', now(), now())", connection);
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
}
