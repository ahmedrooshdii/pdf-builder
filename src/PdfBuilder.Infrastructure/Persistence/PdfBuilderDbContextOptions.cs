using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PdfBuilder.Infrastructure.Persistence;

/// <summary>
/// The one place that says how the context talks to PostgreSQL, shared by the running service and
/// by <c>dotnet ef</c>, so a migration is generated from exactly the model the service uses.
/// </summary>
public static class PdfBuilderDbContextOptions
{
    /// <summary>Npgsql with snake_case table and column names, as data-model.md specifies.</summary>
    public static DbContextOptionsBuilder Configure(this DbContextOptionsBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
    }
}

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without starting the whole service, which
/// would need a broker and a real connection string. The connection string here is never opened:
/// generating a migration only reads the model. It names port 1, where nothing listens, so that a
/// mistake which did try to connect would fail at once instead of reaching a real database.
/// </summary>
public sealed class PdfBuilderDbContextFactory : IDesignTimeDbContextFactory<PdfBuilderDbContext>
{
    public PdfBuilderDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PdfBuilderDbContext>();
        options.Configure("Host=127.0.0.1;Port=1;Database=pdf_builder_design_time;Timeout=1");
        return new PdfBuilderDbContext(options.Options);
    }
}
