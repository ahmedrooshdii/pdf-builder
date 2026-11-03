using Microsoft.EntityFrameworkCore;
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
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PdfBuilderDbContext>();
        var database = db.Database;

        var pending = (await database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        var names = string.Join(", ", pending);

        if (options.Value.MigrateOnStartup)
        {
            await database.MigrateAsync(cancellationToken);

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

        // After the schema, before anything is served: a deployment upgraded from the file store
        // must have its templates by the time the first request arrives. Does nothing after the first time.
        await scope.ServiceProvider.GetRequiredService<TemplateImporter>().ImportAsync(db, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
