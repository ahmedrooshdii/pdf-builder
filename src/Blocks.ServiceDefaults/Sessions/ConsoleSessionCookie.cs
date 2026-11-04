using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Clients;
using Blocks.ServiceDefaults.Problems;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.ServiceDefaults.Sessions;

/// <summary>
/// The <c>ConsoleSession</c> cookie: <c>HttpOnly</c>, <c>Secure</c>, <c>SameSite=Strict</c>, absolute
/// lifetime, and accepted only while its <c>console_sessions</c> row is live and was made with the
/// admin key configured now. The API never redirects: refusals are problem details.
/// </summary>
internal sealed class ConsoleSessionCookie(IOptions<BlocksAuthOptions> blocks, TimeProvider clock)
    : IConfigureNamedOptions<CookieAuthenticationOptions>
{
    /// <summary>Set on the request when a session could not be checked because storage is unreachable.</summary>
    internal static readonly object StorageFailureKey = new();

    public void Configure(CookieAuthenticationOptions options) => Configure(Options.DefaultName, options);

    public void Configure(string? name, CookieAuthenticationOptions options)
    {
        if (name != ConsoleSessionDefaults.Scheme)
            return;

        var settings = blocks.Value;

        options.Cookie.Name = ConsoleSessionDefaults.CookieName(settings.BlockCode);
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = settings.ConsoleSessionLifetime;
        options.SlidingExpiration = false;
        options.TimeProvider = clock;

        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = ValidateAsync,
            OnRedirectToLogin = context => RefuseAsync(context.HttpContext),
            OnRedirectToAccessDenied = context => BlocksProblems.WriteAsync(context.HttpContext, BlocksProblems.ScopeMissing())
        };
    }

    /// <summary>SHA-256 of a session id, as stored.</summary>
    public static byte[] HashSessionId(string sessionId) => SHA256.HashData(Encoding.UTF8.GetBytes(sessionId));

    /// <summary>The first 16 bytes of SHA-256 of the configured admin key hash.</summary>
    public static byte[] AdminKeyFingerprint(string adminKeyHash) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(adminKeyHash))[..16];

    private async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var sessionId = context.Principal?.FindFirstValue(BlocksClaims.SessionId);
        if (string.IsNullOrEmpty(sessionId))
        {
            await RejectAsync(context);
            return;
        }

        var store = context.HttpContext.RequestServices.GetRequiredService<IConsoleSessionStore>();
        ConsoleSession? session;
        try
        {
            session = await store.FindAsync(HashSessionId(sessionId), context.HttpContext.RequestAborted);
        }
        catch (StorageUnavailableException exception)
        {
            // Not "signed out": the cookie stays, so the operator is not sent to sign in by an outage.
            context.HttpContext.Items[StorageFailureKey] = true;
            ConsoleLog.SessionUncheckable(Logger(context.HttpContext), exception);
            context.RejectPrincipal();
            return;
        }

        if (session is null || !IsLive(session))
            await RejectAsync(context);
    }

    private bool IsLive(ConsoleSession session) =>
        session.RevokedAt is null
        && session.ExpiresAt > clock.GetUtcNow()
        && CryptographicOperations.FixedTimeEquals(
            session.AdminKeyFingerprint, AdminKeyFingerprint(blocks.Value.AdminKeyHash!));

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(ConsoleSessionDefaults.Scheme);
    }

    private static Task RefuseAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        return BlocksProblems.WriteAsync(
            context,
            context.Items.ContainsKey(StorageFailureKey) ? BlocksProblems.StorageUnavailable() : BlocksProblems.AuthRequired());
    }

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(ConsoleLog.Category);
}
