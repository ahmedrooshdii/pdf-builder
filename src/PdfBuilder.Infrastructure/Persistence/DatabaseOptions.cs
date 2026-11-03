namespace PdfBuilder.Infrastructure.Persistence;

/// <summary>Settings for the block's own database. The connection string is <c>ConnectionStrings:Database</c>.</summary>
public sealed class DatabaseOptions
{
    public const string Section = "Blocks:Database";

    /// <summary>The configuration key of the connection string, quoted in start-up errors.</summary>
    public const string ConnectionStringName = "Database";

    /// <summary>
    /// Apply pending migrations when the service starts. Turn it off when migrations are applied by
    /// a separate deployment step: the service then refuses to start against a schema that is out
    /// of date, naming the migrations it is missing.
    /// </summary>
    public bool MigrateOnStartup { get; init; } = true;
}
