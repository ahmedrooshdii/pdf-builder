using Microsoft.EntityFrameworkCore;
using Npgsql;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.TestSupport;
using Testcontainers.PostgreSql;

// One PostgreSQL per test run, shared by every test class that asks for it.
[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace PdfBuilder.TestSupport;

/// <summary>
/// A real PostgreSQL for the tests that exercise the stores, the migration, the template import and
/// the outbox. This file is linked into every test project that needs a database.
/// </summary>
/// <remarks>
/// <para>
/// Opt-in, like the browser tests: set <c>PDFBUILDER_RUN_DATABASE_TESTS=1</c>. Without it nothing
/// starts and the tests that need a database skip themselves, so <c>dotnet test</c> stays fast and
/// needs no Docker.
/// </para>
/// <para>
/// The server is a Testcontainers <c>postgres:17-alpine</c>, or any server you already have: set
/// <c>PDFBUILDER_TEST_POSTGRES</c> to a connection string with rights to create databases (for
/// example <c>Host=localhost;Port=5432;Username=postgres;Password=…</c>).
/// </para>
/// <para>
/// Migrations are applied once to a template database, and every test gets its own copy, so tests
/// never share rows and can run in parallel. The copy is dropped when the test is done.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string EnableVariable = "PDFBUILDER_RUN_DATABASE_TESTS";
    public const string ServerVariable = "PDFBUILDER_TEST_POSTGRES";

    // Unique per fixture: every test project has its own fixture, they run at the same time, and with
    // a server named by PDFBUILDER_TEST_POSTGRES they share it.
    private readonly string _templateDatabase = $"pdf_builder_template_{Guid.NewGuid():N}";

    // Cloning one template from several tests at once is allowed, but it is cheap to avoid.
    private readonly SemaphoreSlim _cloneLock = new(1, 1);

    private PostgreSqlContainer? _container;
    private NpgsqlConnectionStringBuilder? _admin;
    private Exception? _startFailure;

    public static bool Enabled => Environment.GetEnvironmentVariable(EnableVariable) == "1";

    /// <summary>Call first in a test that needs the database.</summary>
    public static void SkipUnlessEnabled() => Assert.SkipWhen(
        !Enabled,
        $"Set {EnableVariable}=1 to run tests that need PostgreSQL (Docker, or a server named by {ServerVariable}).");

    public async ValueTask InitializeAsync()
    {
        if (!Enabled)
            return;

        // A fixture that throws here fails every test in the assembly, including the ones that never
        // touch a database. So a failed start is kept and reported by the tests that need the server.
        try
        {
            var external = Environment.GetEnvironmentVariable(ServerVariable);
            if (string.IsNullOrWhiteSpace(external))
            {
                _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
                await _container.StartAsync();
                external = _container.GetConnectionString();
            }

            _admin = new NpgsqlConnectionStringBuilder(external) { Database = "postgres", Pooling = false };

            await ExecuteAsync($"CREATE DATABASE {_templateDatabase}");

            await using var template = Open(_templateDatabase);
            await template.Database.MigrateAsync();
        }
        catch (Exception exception)
        {
            _startFailure = exception;
        }
    }

    /// <summary>A database with the schema already applied, private to the caller.</summary>
    public Task<TestDatabase> CreateDatabaseAsync() => CreateAsync($"TEMPLATE {_templateDatabase}");

    /// <summary>A database with nothing in it, for tests of the migration itself.</summary>
    public Task<TestDatabase> CreateEmptyDatabaseAsync() => CreateAsync("");

    private async Task<TestDatabase> CreateAsync(string clause)
    {
        if (_startFailure is not null)
            throw new InvalidOperationException(
                $"PostgreSQL for the tests could not be started ({_startFailure.Message}). " +
                $"Use Docker, or point {ServerVariable} at a server that can create databases.",
                _startFailure);

        var name = $"pdf_builder_test_{Guid.NewGuid():N}";

        await _cloneLock.WaitAsync();
        try
        {
            await ExecuteAsync($"CREATE DATABASE {name} {clause}");
        }
        finally
        {
            _cloneLock.Release();
        }

        return new TestDatabase(
            new NpgsqlConnectionStringBuilder(_admin!.ConnectionString) { Database = name }.ConnectionString,
            () => ExecuteAsync($"DROP DATABASE IF EXISTS {name} WITH (FORCE)"));
    }

    private PdfBuilderDbContext Open(string database)
    {
        var connection = new NpgsqlConnectionStringBuilder(_admin!.ConnectionString) { Database = database };
        var options = new DbContextOptionsBuilder<PdfBuilderDbContext>();
        options.Configure(connection.ConnectionString);
        return new PdfBuilderDbContext(options.Options);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_admin!.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _cloneLock.Dispose();

        // A container takes its databases with it; a server we were handed must be left clean.
        if (_admin is not null && _container is null)
            await ExecuteAsync($"DROP DATABASE IF EXISTS {_templateDatabase} WITH (FORCE)");

        if (_container is not null)
            await _container.DisposeAsync();
    }
}

/// <summary>A clock a test moves by hand, for retention and expiry rules.</summary>
public sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>One test's private database. Disposing it drops the database.</summary>
public sealed class TestDatabase(string connectionString, Func<Task> drop) : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;

    public PdfBuilderDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PdfBuilderDbContext>();
        options.Configure(ConnectionString);
        return new PdfBuilderDbContext(options.Options);
    }

    public async ValueTask DisposeAsync() => await drop();
}
