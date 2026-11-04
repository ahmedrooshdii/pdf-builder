using System.Net.Http.Json;
using System.Text.Json;
using Blocks.ServiceDefaults;
using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Tests.Support;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Api.Tests.Support;

/// <summary>
/// The whole service in process, as deployed: the real <c>Program</c>, its authentication pipeline and
/// a private PostgreSQL database. Only the broker is replaced, by MassTransit's in-memory harness.
/// </summary>
/// <remarks>
/// Runs as <c>Production</c>, so it never loads the developer's user-secrets and can never reach a real
/// broker. Opt-in like every database test: call <see cref="PostgresFixture.SkipUnlessEnabled"/> first.
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The admin key every factory accepts; its hash is what the service is configured with.</summary>
    public static readonly string AdminKey = ApiKeyFormat.NewAdminKey();

    private readonly TestDatabase _database;
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), $"pdf-builder-api-tests-{Guid.NewGuid():N}");
    private readonly Action<IServiceCollection>? _configure;

    private ApiFactory(TestDatabase database, Action<IServiceCollection>? configure)
    {
        _database = database;
        _configure = configure;
    }

    public TestDatabase Database => _database;

    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>A database of its own, migrated, and the service started over it.</summary>
    public static async Task<ApiFactory> StartAsync(PostgresFixture postgres, Action<IServiceCollection>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(postgres);

        var factory = new ApiFactory(await postgres.CreateDatabaseAsync(), configure);
        _ = factory.Server;     // starts the host, so a start-up failure surfaces here
        return factory;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Production");
        builder.UseSetting($"ConnectionStrings:{DatabaseOptions.ConnectionStringName}", _database.ConnectionString);
        builder.UseSetting(BlocksAuthOptions.AdminKeyHashKey, KeyHasher.HashAdminKey(AdminKey));
        builder.UseSetting("Storage:RootPath", Path.Combine(_dataRoot, "files"));
        builder.UseSetting("TemplateStore:RootPath", Path.Combine(RepositoryRoot(), "templates"));
        builder.UseSetting("TemplateStore:AuthoredRootPath", Path.Combine(_dataRoot, "templates"));

        builder.ConfigureLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Debug));

        builder.ConfigureTestServices(services =>
        {
            // The service's consumers and outbox, on an in-memory transport instead of RabbitMQ.
            services.AddMassTransitTestHarness();
            _configure?.Invoke(services);
        });
    }

    public HttpClient ClientWithKey(string? key)
    {
        var client = CreateClient();
        if (key is not null)
            client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, key);
        return client;
    }

    public HttpClient AdminClient() => ClientWithKey(AdminKey);

    /// <summary>Registers the client if it is new, issues it a key, and returns the issue response.</summary>
    public async Task<JsonElement> IssueKeyAsync(
        string clientId, IReadOnlyList<string> scopes, CancellationToken ct, DateTimeOffset? expiresAt = null)
    {
        using var admin = AdminClient();

        using var registered = await admin.PostAsJsonAsync("/api/admin/clients", new { id = clientId, name = clientId }, ct);
        Assert.True(registered.IsSuccessStatusCode || (int)registered.StatusCode == 409, $"Registering {clientId}: {registered.StatusCode}");

        using var issued = await admin.PostAsJsonAsync(
            $"/api/admin/clients/{clientId}/keys", new { name = "test", scopes, expiresAt }, ct);
        Assert.Equal(201, (int)issued.StatusCode);
        return await issued.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    /// <summary>A key with every permission, for calls that only need to get through.</summary>
    public async Task<string> FullKeyAsync(string clientId, CancellationToken ct) =>
        (await IssueKeyAsync(clientId, ["pdf:generate", "pdf:templates:read", "pdf:templates:write"], ct))
        .GetProperty("key").GetString()!;

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();

        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PdfBuilder.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("The repository root (PdfBuilder.slnx) was not found above the test output.");
    }
}
