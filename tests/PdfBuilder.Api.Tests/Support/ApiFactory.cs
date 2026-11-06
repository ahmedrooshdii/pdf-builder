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

    /// <summary>The file-link signing key every factory is configured with; a setting passed to the factory replaces it.</summary>
    public const string FileSigningKey = "dGVzdC1maWxlLXNpZ25pbmcta2V5LTAxMjM0NTY3ODlhYmNkZWY=";

    private readonly TestDatabase _database;
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), $"pdf-builder-api-tests-{Guid.NewGuid():N}");
    private readonly Action<IServiceCollection>? _configure;
    private readonly bool _ownsDatabase;
    private readonly string _adminKeyHash;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    private ApiFactory(
        TestDatabase database, Action<IServiceCollection>? configure, bool ownsDatabase, string? adminKeyHash,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        _settings = settings ?? new Dictionary<string, string?>();
        _database = database;
        _configure = configure;
        _ownsDatabase = ownsDatabase;
        _adminKeyHash = adminKeyHash ?? KeyHasher.HashAdminKey(AdminKey);
    }

    public TestDatabase Database => _database;

    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>A database of its own, migrated, and the service started over it.</summary>
    public static async Task<ApiFactory> StartAsync(PostgresFixture postgres, Action<IServiceCollection>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(postgres);

        var factory = new ApiFactory(await postgres.CreateDatabaseAsync(), configure, ownsDatabase: true, adminKeyHash: null);
        _ = factory.Server;     // starts the host, so a start-up failure surfaces here
        return factory;
    }

    /// <summary>
    /// Another instance of the service over <paramref name="database"/>, which it does not drop: two of
    /// these are two instances behind one database, or one instance restarted.
    /// <paramref name="adminKeyHash"/> replaces the configured admin key hash.
    /// </summary>
    /// <param name="settings">Extra configuration, such as <c>Blocks:Bus:DefaultClientId</c>.</param>
    /// <param name="configure">Replaces services, as for <see cref="StartAsync"/>.</param>
    public static ApiFactory StartOn(
        TestDatabase database, string? adminKeyHash = null,
        IReadOnlyDictionary<string, string?>? settings = null, Action<IServiceCollection>? configure = null)
    {
        var factory = new ApiFactory(database, configure, ownsDatabase: false, adminKeyHash, settings);
        _ = factory.Server;
        return factory;
    }

    /// <summary>Signs in with the admin key and returns the console cookie as <c>name=value</c>.</summary>
    public async Task<string> SignInAsync(CancellationToken ct)
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/console/session")
        {
            Content = JsonContent.Create(new { adminKey = AdminKey })
        };
        request.Headers.Add(ConsoleSessionDefaults.HeaderName, ConsoleSessionDefaults.HeaderValue);

        using var response = await client.SendAsync(request, ct);
        Assert.Equal(204, (int)response.StatusCode);
        return response.Headers.GetValues("Set-Cookie")
            .Single(v => v.StartsWith(ConsoleSessionDefaults.CookieName("pdf") + "=", StringComparison.Ordinal))
            .Split(';', 2)[0];
    }

    /// <summary>
    /// A client sending the console cookie and <c>X-Console: 1</c>. The test server speaks plain HTTP, where
    /// a browser would not send a <c>Secure</c> cookie, so the header is set by hand.
    /// </summary>
    public HttpClient SessionClient(string cookie, string? actingAs = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        client.DefaultRequestHeaders.Add(ConsoleSessionDefaults.HeaderName, ConsoleSessionDefaults.HeaderValue);
        if (actingAs is not null)
            client.DefaultRequestHeaders.Add(ConsoleSessionDefaults.ClientIdHeaderName, actingAs);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Production");
        builder.UseSetting($"ConnectionStrings:{DatabaseOptions.ConnectionStringName}", _database.ConnectionString);
        builder.UseSetting(BlocksAuthOptions.AdminKeyHashKey, _adminKeyHash);
        builder.UseSetting("Storage:RootPath", Path.Combine(_dataRoot, "files"));
        builder.UseSetting("TemplateStore:RootPath", Path.Combine(RepositoryRoot(), "templates"));
        builder.UseSetting("TemplateStore:AuthoredRootPath", Path.Combine(_dataRoot, "templates"));
        builder.UseSetting("Blocks:Files:SigningKey", FileSigningKey);
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);

        builder.ConfigureLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Debug));

        builder.ConfigureTestServices(services =>
        {
            // The service's consumers and outbox, on an in-memory transport instead of RabbitMQ.
            // Registering the harness replaces the service's own bus registration, outbox included, so the
            // outbox is declared again here, as the service declares it (MessagingExtensions.AddMessaging).
            // Without it a request publishes straight to the bus: a worker can start before the job it works on
            // is saved, which production, with the outbox, cannot do.
            services.AddMassTransitTestHarness(x => x.AddEntityFrameworkOutbox<PdfBuilderDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
            }));
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

    /// <summary>The permissions of a key that can do everything a client can.</summary>
    public static readonly string[] AllPermissions = ["pdf:generate", "pdf:templates:read", "pdf:templates:write"];

    /// <summary>Registers the client when it is new. A client that exists is left as it is.</summary>
    public async Task EnsureClientAsync(string clientId, CancellationToken ct)
    {
        using var admin = AdminClient();
        using var registered = await admin.PostAsJsonAsync("/api/admin/clients", new { id = clientId, name = clientId }, ct);
        Assert.True(registered.IsSuccessStatusCode || (int)registered.StatusCode == 409, $"Registering {clientId}: {registered.StatusCode}");
    }

    /// <summary>
    /// A caller with a fresh key of <paramref name="clientId"/> (registered when new), holding every
    /// permission unless <paramref name="scopes"/> says otherwise.
    /// </summary>
    public async Task<HttpClient> ClientForAsync(string clientId, CancellationToken ct, params string[] scopes)
    {
        var issued = await IssueKeyAsync(clientId, scopes.Length > 0 ? scopes : AllPermissions, ct);
        return ClientWithKey(issued.GetProperty("key").GetString());
    }

    /// <summary>The operator working inside <paramref name="clientId"/>, which is registered when new.</summary>
    public async Task<HttpClient> AdminActingAsAsync(string clientId, CancellationToken ct)
    {
        await EnsureClientAsync(clientId, ct);
        var client = AdminClient();
        client.DefaultRequestHeaders.Add(ConsoleSessionDefaults.ClientIdHeaderName, clientId);
        return client;
    }

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
        if (_ownsDatabase)
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


    /// <summary>
    /// Polls a job until it is no longer <c>Pending</c> and returns its status response. The worker runs in
    /// the same process on an in-memory bus, so this takes well under a second; the limit is only there so a
    /// stuck job fails the test instead of hanging it.
    /// </summary>
    public static async Task<JsonElement> WaitForJobAsync(HttpClient client, Guid jobId, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            var job = await client.GetFromJsonAsync<JsonElement>($"/api/pdf/jobs/{jobId}", ct);
            if (job.GetProperty("status").GetString() != "Pending")
                return job;

            Assert.True(DateTimeOffset.UtcNow < deadline, $"Job {jobId} was still Pending after 30 seconds.");
            await Task.Delay(50, ct);
        }
    }

    /// <summary>Submits a job as <paramref name="client"/> and returns its id.</summary>
    public static async Task<Guid> SubmitJobAsync(HttpClient client, string templateKey, object data, CancellationToken ct)
    {
        using var accepted = await client.PostAsJsonAsync("/api/pdf/jobs", new { templateKey, data }, ct);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, accepted.StatusCode);
        return (await accepted.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("jobId").GetGuid();
    }
}
