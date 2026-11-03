using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Infrastructure.Tests;

/// <summary>
/// FR-017: when the database cannot be reached the service reports itself not ready, through the
/// readiness checks it already has. (Liveness is a separate endpoint that runs no checks.)
/// </summary>
public sealed class DatabaseHealthCheckTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider Service(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Database"] = connectionString })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
    }

    private static Task<HealthReport> ReadyAsync(ServiceProvider provider) =>
        provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Tags.Contains("ready"), Ct);

    [Fact]
    public async Task An_unreachable_database_makes_the_service_not_ready()
    {
        // Nothing listens on port 1, so this needs no server and fails fast.
        await using var provider = Service("Host=127.0.0.1;Port=1;Username=nobody;Database=nothing;Timeout=3");

        var report = await ReadyAsync(provider);

        Assert.Equal(HealthStatus.Unhealthy, report.Entries["database"].Status);
        Assert.Equal(HealthStatus.Unhealthy, report.Status);
    }

    [Fact]
    public async Task The_failure_names_the_database_check_without_repeating_the_connection_string()
    {
        await using var provider = Service("Host=127.0.0.1;Port=1;Username=nobody;Password=hunter2;Database=nothing;Timeout=3");

        var entry = (await ReadyAsync(provider)).Entries["database"];

        Assert.DoesNotContain("hunter2", entry.Description ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", entry.Exception?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reachable_database_is_ready()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var provider = Service(database.ConnectionString);

        var report = await ReadyAsync(provider);

        Assert.Equal(HealthStatus.Healthy, report.Entries["database"].Status);
    }

    [Fact]
    public void A_missing_connection_string_stops_the_service_and_names_the_setting()
    {
        var configuration = new ConfigurationBuilder().Build();

        var error = Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() =>
            new ServiceCollection().AddInfrastructure(configuration));

        Assert.Contains("ConnectionStrings:Database", error.Message, StringComparison.Ordinal);
    }
}
