using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Clients;
using Blocks.ServiceDefaults.Problems;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.ServiceDefaults.Sessions;

/// <summary>
/// <c>/api/console/session</c>: the operator signs in with the admin key, asks when the session ends,
/// and signs out. Every block's console signs in the same way.
/// </summary>
internal static class ConsoleSessionEndpoints
{
    public const string Path = "/api/console/session";

    /// <summary>The policy of <c>GET</c>: a console session, and nothing else (an admin key is not a session).</summary>
    public const string SessionPolicy = "blocks:console-session";

    // A sign-in body is a 64-character key in a small JSON object; anything much larger is not one.
    private const int MaxBodyBytes = 1024;

    // Sessions that ended more than this long ago are deleted at the next sign-in.
    private static readonly TimeSpan RetainExpired = TimeSpan.FromDays(1);

    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Path).WithTags("Console");

        group.MapPost("/", SignInAsync)
            .AllowAnonymous()
            .WithName("SignIn")
            .WithSummary("Sign in to the console with the admin key; sets the session cookie")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/", (Delegate)GetSessionAsync)
            .RequireAuthorization(SessionPolicy)
            .WithName("GetSession")
            .WithSummary("When the current console session ends")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapDelete("/", SignOutAsync)
            .AllowAnonymous()
            .WithName("SignOut")
            .WithSummary("Sign out: the session stops working on every instance and the cookie is cleared")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> SignInAsync(
        HttpContext context,
        IConsoleSessionStore store,
        SignInThrottle throttle,
        IOptions<BlocksAuthOptions> blocks,
        TimeProvider clock,
        ILoggerFactory loggers)
    {
        NoStore(context);
        var logger = loggers.CreateLogger(ConsoleLog.Category);

        if (!ConsoleHeaderMiddleware.HasConsoleHeader(context.Request))
            return BlocksProblems.Result(BlocksProblems.ConsoleHeaderRequired());

        // Before the body is read: an address over the limit costs nothing more than this lookup.
        var address = context.Connection.RemoteIpAddress ?? IPAddress.None;
        if (!throttle.TryEnter(address, out var retryAfter))
        {
            ConsoleLog.SignInRefused(logger, "Throttled");
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            return BlocksProblems.Result(BlocksProblems.RateLimited());
        }

        var settings = blocks.Value;
        var adminKey = await ReadAdminKeyAsync(context.Request, context.RequestAborted);
        if (!IsAdminKey(adminKey, settings.AdminKeyHash!))
        {
            throttle.RecordFailure(address);
            ConsoleLog.SignInRefused(logger, "Invalid");
            return BlocksProblems.Result(BlocksProblems.InvalidKey());
        }

        var now = clock.GetUtcNow();
        var expiresAt = now + settings.ConsoleSessionLifetime;
        var sessionId = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        try
        {
            await store.DeleteExpiredBeforeAsync(now - RetainExpired, context.RequestAborted);
            await store.CreateAsync(
                new ConsoleSession(
                    ConsoleSessionCookie.HashSessionId(sessionId),
                    now,
                    expiresAt,
                    RevokedAt: null,
                    ConsoleSessionCookie.AdminKeyFingerprint(settings.AdminKeyHash!)),
                context.RequestAborted);
        }
        catch (StorageUnavailableException exception)
        {
            ConsoleLog.SessionUncheckable(logger, exception);
            return StorageUnavailable(context);
        }

        // The identity's authentication type is the scheme: that is how the X-Console rule recognises it.
        var identity = new ClaimsIdentity(
            [
                new Claim(BlocksClaims.Scope, BlocksClaims.AdminScope),
                new Claim(BlocksClaims.SessionId, sessionId)
            ],
            ConsoleSessionDefaults.Scheme);

        await context.SignInAsync(
            ConsoleSessionDefaults.Scheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = false,
                IssuedUtc = now,
                ExpiresUtc = expiresAt
            });

        ConsoleLog.SessionStarted(logger, expiresAt);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetSessionAsync(HttpContext context)
    {
        NoStore(context);

        // Already authenticated by the policy; the handler caches its result for the request.
        var result = await context.AuthenticateAsync(ConsoleSessionDefaults.Scheme);
        return TypedResults.Ok(new ConsoleSessionResponse(result.Properties?.ExpiresUtc));
    }

    private static async Task<IResult> SignOutAsync(HttpContext context, IConsoleSessionStore store, TimeProvider clock, ILoggerFactory loggers)
    {
        NoStore(context);
        var logger = loggers.CreateLogger(ConsoleLog.Category);

        if (!ConsoleHeaderMiddleware.HasConsoleHeader(context.Request))
            return BlocksProblems.Result(BlocksProblems.ConsoleHeaderRequired());

        var result = await context.AuthenticateAsync(ConsoleSessionDefaults.Scheme);

        // The session could not be checked, so it may still be live: keep the cookie and let the operator retry.
        if (context.Items.ContainsKey(ConsoleSessionCookie.StorageFailureKey))
            return StorageUnavailable(context);

        var sessionId = result.Principal?.FindFirstValue(BlocksClaims.SessionId);
        if (result.Succeeded && !string.IsNullOrEmpty(sessionId))
        {
            try
            {
                await store.RevokeAsync(ConsoleSessionCookie.HashSessionId(sessionId), clock.GetUtcNow(), context.RequestAborted);
            }
            catch (StorageUnavailableException exception)
            {
                ConsoleLog.SessionUncheckable(logger, exception);
                return StorageUnavailable(context);
            }

            ConsoleLog.SessionEnded(logger);
        }

        await context.SignOutAsync(ConsoleSessionDefaults.Scheme);
        return TypedResults.NoContent();
    }

    /// <summary>The <c>adminKey</c> member of a small JSON body, or null for anything else.</summary>
    private static async Task<string?> ReadAdminKeyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength is > MaxBodyBytes || !request.HasJsonContentType())
            return null;

        try
        {
            var body = await request.ReadFromJsonAsync<SignInRequest>(ct);
            return body?.AdminKey;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (BadHttpRequestException)
        {
            return null;
        }
    }

    private static bool IsAdminKey(string? value, string adminKeyHash)
    {
        if (string.IsNullOrEmpty(value) || value.Length > ApiKeyFormat.MaxLength)
            return false;

        return KeyHasher.Matches(Convert.FromHexString(adminKeyHash), Convert.FromHexString(KeyHasher.HashAdminKey(value)));
    }

    private static IResult StorageUnavailable(HttpContext context)
    {
        context.Response.Headers.RetryAfter = BlocksProblems.StorageRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        return BlocksProblems.Result(BlocksProblems.StorageUnavailable());
    }

    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";
}

/// <summary>The sign-in body.</summary>
internal sealed record SignInRequest(string? AdminKey);

/// <summary>The answer of <c>GET /api/console/session</c>.</summary>
public sealed record ConsoleSessionResponse(DateTimeOffset? ExpiresAt);

/// <summary>Source-generated log messages for console sessions. None of them ever takes a key or a session id.</summary>
internal static partial class ConsoleLog
{
    public const string Category = "Blocks.ServiceDefaults.Sessions.ConsoleSession";

    [LoggerMessage(EventId = 9100, Level = LogLevel.Information, Message = "Console session started, ends {ExpiresAt}")]
    public static partial void SessionStarted(ILogger logger, DateTimeOffset expiresAt);

    [LoggerMessage(EventId = 9101, Level = LogLevel.Debug, Message = "Console sign-in refused: {Reason}")]
    public static partial void SignInRefused(ILogger logger, string reason);

    [LoggerMessage(EventId = 9102, Level = LogLevel.Information, Message = "Console session ended")]
    public static partial void SessionEnded(ILogger logger);

    [LoggerMessage(EventId = 9103, Level = LogLevel.Warning, Message = "Console session could not be checked: storage is unavailable")]
    public static partial void SessionUncheckable(ILogger logger, Exception exception);
}
