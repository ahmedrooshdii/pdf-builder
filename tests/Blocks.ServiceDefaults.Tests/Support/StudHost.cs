using System.Net.Http.Json;
using System.Text.Json;
using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Clients;
using Blocks.ServiceDefaults.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blocks.ServiceDefaults.Tests.Support;

/// <summary>
/// A small block, in process, that takes the stud exactly as a real one does: the handler, the
/// policies, the fallback, the log scope and the management endpoints, over an in-memory store.
/// </summary>
public sealed partial class StudHost : IAsyncDisposable
{
    public const string BlockCode = "pdf";
    public const string Generate = "pdf:generate";
    public const string TemplatesRead = "pdf:templates:read";
    public const string TemplatesWrite = "pdf:templates:write";

    public static readonly IReadOnlyList<string> Permissions = [Generate, TemplatesRead, TemplatesWrite];

    /// <summary>The admin key every host accepts; its hash is what the host is configured with.</summary>
    public static readonly string AdminKey = ApiKeyFormat.NewAdminKey();

    private readonly WebApplication _app;

    private StudHost(WebApplication app, InMemoryClientKeyStore store, ManualClock clock, CapturingLoggerProvider logs)
    {
        _app = app;
        Store = store;
        Clock = clock;
        Logs = logs;
    }

    public InMemoryClientKeyStore Store { get; }
    public ManualClock Clock { get; }
    public CapturingLoggerProvider Logs { get; }
    public IServiceProvider Services => _app.Services;

    /// <summary>
    /// Endpoints: <c>/generate</c> (pdf:generate), <c>/templates</c> (read), <c>/templates/edit</c>
    /// (write), <c>/plain</c> (no metadata, so the fallback policy applies), <c>/open</c> (anonymous),
    /// <c>/whoami</c> (pdf:generate, returns <see cref="ICurrentClient"/>), and <c>/api/admin/…</c>.
    /// <paramref name="customStore"/>, when given, replaces the in-memory store the host is given.
    /// </summary>
    public static async Task<StudHost> StartAsync(
        InMemoryClientKeyStore? store = null,
        ManualClock? clock = null,
        string? adminKeyHash = null,
        IClientKeyStore? customStore = null)
    {
        store ??= new InMemoryClientKeyStore();
        clock ??= new ManualClock(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        var logs = new CapturingLoggerProvider();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration[BlocksAuthOptions.AdminKeyHashKey] = adminKeyHash ?? KeyHasher.HashAdminKey(AdminKey);
        builder.Logging.ClearProviders().AddProvider(logs).SetMinimumLevel(LogLevel.Debug);

        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton(customStore ?? store);
        builder.Services.AddBlocksAuthentication(BlockCode, Permissions);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseBlocksClientLogScope();

        app.MapGet("/generate", (ILogger<StudHost> logger) =>
        {
            Generating(logger);
            return "generated";
        }).RequirePermission(Generate);
        app.MapGet("/templates", () => "listed").RequirePermission(TemplatesRead);
        app.MapPost("/templates/edit", () => "edited").RequirePermission(TemplatesWrite);
        app.MapGet("/plain", () => "plain");
        app.MapGet("/open", () => "open").AllowAnonymous();
        app.MapGet("/whoami", (ICurrentClient caller) => new
        {
            caller.ClientId,
            caller.KeyId,
            Scopes = caller.Scopes.Order(StringComparer.Ordinal).ToArray(),
            caller.IsAdmin
        }).RequirePermission(Generate);
        app.MapBlocksAdmin();

        await app.StartAsync();
        return new StudHost(app, store, clock, logs);
    }

    public HttpClient Client(string? key = null)
    {
        var client = _app.GetTestClient();
        if (key is not null)
            client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, key);
        return client;
    }

    public HttpClient Admin() => Client(AdminKey);

    /// <summary>Registers a client through the management API.</summary>
    public async Task RegisterAsync(string clientId, CancellationToken ct)
    {
        var response = await Admin().PostAsJsonAsync("/api/admin/clients", new { id = clientId, name = clientId }, ct);
        Assert.Equal(StatusCodes.Status201Created, (int)response.StatusCode);
    }

    /// <summary>Issues a key through the management API and returns the issue response.</summary>
    public async Task<JsonElement> IssueAsync(
        string clientId, IReadOnlyList<string> scopes, CancellationToken ct, DateTimeOffset? expiresAt = null)
    {
        var response = await Admin().PostAsJsonAsync(
            $"/api/admin/clients/{clientId}/keys", new { name = "production", scopes, expiresAt }, ct);
        Assert.Equal(StatusCodes.Status201Created, (int)response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    /// <summary>A line written inside an endpoint, to show which scope it carries.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "generating")]
    private static partial void Generating(ILogger logger);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>A clock a test moves by hand.</summary>
public sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
