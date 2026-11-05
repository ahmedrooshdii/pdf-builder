using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfBuilder.Infrastructure.Templates;

namespace PdfBuilder.Infrastructure.Persistence;

/// <summary>
/// Brings the database to the shape this version expects before anything else runs.
/// </summary>
/// <remarks>
/// Registered ahead of the message bus and the web server, and hosted services start in
/// registration order, so by the time a request or a message arrives the schema is current. When
/// several instances start together, EF Core's migration lock lets exactly one apply a migration.
/// </remarks>
internal sealed class DatabaseStartup(
    IServiceScopeFactory scopes,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseStartup> logger) : IHostedService
{
    // The two halves of the upgrade to per-client data (research R6). Matched by name, because a migration's
    // id starts with the time it was generated.
    internal const string AddClientOwner = "_AddClientOwner";
    internal const string RequireClientOwner = "_RequireClientOwner";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PdfBuilderDbContext>();
        var database = db.Database;

        var pending = (await database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        var names = string.Join(", ", pending);

        var importer = scope.ServiceProvider.GetRequiredService<TemplateImporter>();

        if (options.Value.MigrateOnStartup)
        {
            if (pending.Any(id => id.EndsWith(RequireClientOwner, StringComparison.Ordinal)))
            {
                // Templates and jobs from before clients existed get an owner between the two migrations,
                // because only a running service knows who that is: it comes from configuration. Stopping at
                // AddClientOwner is safe when the database is already there or behind it; it would not be if
                // it were past it, which cannot be the case while RequireClientOwner is still pending.
                var addClientOwner = database.GetMigrations().Single(id => id.EndsWith(AddClientOwner, StringComparison.Ordinal));
                await database.GetService<IMigrator>().MigrateAsync(addClientOwner, cancellationToken);

                await importer.ImportAsync(db, cancellationToken);
                await scope.ServiceProvider.GetRequiredService<ClientOwnerAssignment>()
                    .AssignUnownedToDefaultClientAsync(db, cancellationToken);

                await database.MigrateAsync(cancellationToken);
            }
            else
            {
                await database.MigrateAsync(cancellationToken);

                // After the schema, before anything is served: a deployment upgraded from the file store
                // must have its templates by the time the first request arrives. Does nothing after the first time.
                await importer.ImportAsync(db, cancellationToken);
            }

            if (pending.Count > 0)
                PersistenceLog.MigrationsApplied(logger, names);
            else
                PersistenceLog.SchemaUpToDate(logger);
        }
        else if (pending.Count > 0)
        {
            throw new InvalidOperationException(
                $"The database schema is out of date and {DatabaseOptions.Section}:{nameof(DatabaseOptions.MigrateOnStartup)} " +
                $"is false. Pending migrations: {names}. Apply them, or set the option to true.");
        }
        else
        {
            // Nothing to migrate, and the operator manages the schema: the import still has to have happened.
            await importer.ImportAsync(db, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
