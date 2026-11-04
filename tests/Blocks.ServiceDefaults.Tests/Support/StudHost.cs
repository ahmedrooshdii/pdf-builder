using System.Net.Http.Json;
using System.Text.Json;
using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Clients;
using Blocks.ServiceDefaults.Identity;
using Blocks.ServiceDefaults.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blocks.ServiceDefaults.Tests.Support;

/// <summary>
/// A small block, in process, that takes the stud exactly as a real one does: the handlers, the
/// policies, the fallback, the console rules, the log scope, the management and console endpoints, over
/// in-memory stores.
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

    /// <summary>The console session cookie's name for <see cref="BlockCode"/>.</summary>
    public static readonly string CookieName = ConsoleSessionDefaults.CookieName(BlockCode);

    /// <summary>
    /// One key ring for every host in the test run, standing for the ring in a shared database: a cookie
    /// made by one host opens on another, and on a host started after it.
    /// </summary>
    private static readonly IDataProtectionProvider SharedKeyRing = new EphemeralDataProtectionProvider();

    private readonly WebApplication _app;

    private StudHost(
        WebApplication app,
        InMemoryClientKeyStore store,
        InMemoryConsoleSessionStore sessions,
        ManualClock clock,
        CapturingLoggerProvider logs)
    {
        _app = app;
        Store = store;
        Sessions = sessions;
        Clock = clock;
        Logs = logs;
    }

    public InMemoryClientKeyStore Store { get; }
    public InMemoryConsoleSessionStore Sessions { get; }
    public ManualClock Clock { get; }
    public CapturingLoggerProvider Logs { get; }
    public IServiceProvider Services => _app.Services;

    /// <summary>
    /// Endpoints: <c>/generate</c> (pdf:generate), <c>/templates</c> (read), <c>/templates/edit</c>
    /// (write, POST), <c>/plain</c> (no metadata, so the fallback policy applies), <c>/open</c> (anonymous),
    /// <c>/whoami</c> (pdf:generate, returns <see cref="ICurrentClient"/>), <c>/api/admin/…</c> and
    /// <c>/api/console/session</c>. <paramref name="customStore"/>, when given, replaces the in-memory
    /// key store the host is given. <paramref name="settings"/> are added to configuration.
    /// </summary>
    public static async Task<StudHost> StartAsync(
        InMemoryClientKeyStore? store = null,
        ManualClock? clock = null,
        string? adminKeyHash = null,
        IClientKeyStore? customStore = null,
        InMemoryConsoleSessionStore? sessions = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        store ??= new InMemoryClientKeyStore();
        sessions ??= new InMemoryConsoleSessionStore();
        clock ??= new ManualClock(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        var logs = new CapturingLoggerProvider();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration[BlocksAuthOptions.AdminKeyHashKey] = adminKeyHash ?? KeyHasher.HashAdminKey(AdminKey);
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            builder.Configuration[key] = value;
        builder.Logging.ClearProviders().AddProvider(logs).SetMinimumLevel(LogLevel.Debug);

        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton(customStore ?? store);
        builder.Services.AddSingleton<IConsoleSessionStore>(sessions);
        builder.Services.AddSingleton(SharedKeyRing);
        builder.Services.AddBlocksAuthentication(BlockCode, Permissions);

        var app = builder.Build();
        app.UseBlocksAuthentication();

        app.MapGet("/generate", (ILogger<StudHost> logger) =>
        {
            Generating(logger);
            return "generated";
        }).RequirePermission(Generate);
        app.MapGet("/templates", () => "listed").RequirePermission(TemplatesRead);
        app.MapPost("/templates/edit", () => "edited").RequirePermission(TemplatesWrite);
        app.MapPatch("/templates/edit", () => "edited").RequirePermission(TemplatesWrite);
        app.MapDelete("/templates/edit", () => "deleted").RequirePermission(TemplatesWrite);
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
        app.MapBlocksConsole();

        await app.StartAsync();
        return new StudHost(app, store, sessions, clock, logs);
    }

    public HttpClient Client(string? key = null)
    {
        var client = _app.GetTestClient();
        if (key is not null)
            client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, key);
        return client;
    }

    public HttpClient Admin() => Client(AdminKey);

    /// <summary>
    /// A client that sends the console cookie <paramref name="cookie"/> (the <c>name=value</c> pair), and
    /// <c>X-Console: 1</c> unless <paramref name="consoleHeader"/> is false. The test server speaks plain
    /// HTTP, where a real browser would not send a <c>Secure</c> cookie, so the header is set by hand.
    /// </summary>
    public HttpClient Session(string cookie, bool consoleHeader = true)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        if (consoleHeader)
            client.DefaultRequestHeaders.Add(ConsoleSessionDefaults.HeaderName, ConsoleSessionDefaults.HeaderValue);
        return client;
    }

    /// <summary>Posts a sign-in and returns the response, with <c>X-Console: 1</c> unless told otherwise.</summary>
    public async Task<HttpResponseMessage> PostSignInAsync(string? adminKey, CancellationToken ct, bool consoleHeader = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/console/session")
        {
            Content = JsonContent.Create(new { adminKey })
        };
        if (consoleHeader)
            request.Headers.Add(ConsoleSessionDefaults.HeaderName, ConsoleSessionDefaults.HeaderValue);
        return await Client().SendAsync(request, ct);
    }

    /// <summary>Signs in with the admin key and returns the cookie as <c>name=value</c>.</summary>
    public async Task<string> SignInAsync(CancellationToken ct)
    {
        using var response = await PostSignInAsync(AdminKey, ct);
        Assert.Equal(StatusCodes.Status204NoContent, (int)response.StatusCode);
        return SessionCookie(response) ?? throw new InvalidOperationException("Sign-in set no cookie.");
    }

    /// <summary>The <c>Set-Cookie</c> header for the console cookie, whole, or null.</summary>
    public static string? SetCookieHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(CookieName + "=", StringComparison.Ordinal))
            : null;

    /// <summary>The console cookie as <c>name=value</c>, or null when the response sets none (or clears it).</summary>
    public static string? SessionCookie(HttpResponseMessage response)
    {
        var header = SetCookieHeader(response);
        var pair = header?.Split(';', 2)[0];
        return pair is null || pair.Length == CookieName.Length + 1 ? null : pair;
    }

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
