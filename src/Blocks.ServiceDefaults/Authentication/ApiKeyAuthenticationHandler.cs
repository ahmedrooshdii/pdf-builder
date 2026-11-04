using System.Security.Claims;
using System.Text.Encodings.Web;
using Blocks.ServiceDefaults.Clients;
using Blocks.ServiceDefaults.Problems;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.ServiceDefaults.Authentication;

/// <summary>Why a request did not authenticate. Decides the refusal, and nothing else.</summary>
internal enum ApiKeyFailure
{
    /// <summary>No <c>X-Api-Key</c> header.</summary>
    Missing,

    /// <summary>Unknown, malformed, revoked or expired, or the header sent twice: deliberately one case.</summary>
    Invalid,

    /// <summary>A key that is otherwise usable, whose client is disabled.</summary>
    ClientDisabled,

    /// <summary>The key could not be checked because storage is unreachable.</summary>
    StorageUnavailable,

    /// <summary>Anything else that went wrong while checking.</summary>
    Error
}

/// <summary>
/// Turns <c>X-Api-Key</c> into a principal: <c>client_id</c>, <c>key_id</c> and one <c>scope</c> per
/// permission for a client key; only <c>scope=admin</c> for the admin key.
/// </summary>
/// <remarks>
/// <para>
/// Every non-success is classified here and recorded on the request, and the refusal is written
/// from that record by <see cref="HandleChallengeAsync"/>. Exceptions are never left to the
/// authentication middleware, which would turn a database outage into "invalid key".
/// </para>
/// <para>
/// The checks run in a fixed order: the secret must match before anything about the key or its client
/// is revealed, so <c>CLIENT_DISABLED</c> cannot confirm that a key id exists to someone without its
/// secret. Unknown, wrong-secret, revoked and expired all become <see cref="ApiKeyFailure.Invalid"/>.
/// </para>
/// </remarks>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptions<BlocksAuthOptions> blocks,
    KeyCache cache,
    KeyUsageRecorder usage,
    TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    private static readonly object FailureKey = new();

    // One dummy hash, compared when a key id is unknown, so that path costs what a known one does.
    private static readonly byte[] UnknownKeyHash = new byte[32];

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var values))
        {
            Context.Items[FailureKey] = ApiKeyFailure.Missing;
            return AuthenticateResult.NoResult();
        }

        // A header sent twice arrives as two values; which one was meant is not ours to guess.
        if (values.Count != 1)
            return Refuse(ApiKeyFailure.Invalid);

        var key = ApiKeyFormat.Parse(values[0], blocks.Value.BlockCode);

        try
        {
            return key.Kind switch
            {
                ApiKeyKind.Admin => AuthenticateAdmin(values[0]!),
                ApiKeyKind.Client => await AuthenticateClientAsync(key.KeyId!, key.Secret!),
                _ => Refuse(ApiKeyFailure.Invalid)
            };
        }
        catch (StorageUnavailableException)
        {
            AuthLog.StorageUnavailable(Logger, key.KeyId);
            return Refuse(ApiKeyFailure.StorageUnavailable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AuthLog.CheckFailed(Logger, key.KeyId, exception);
            return Refuse(ApiKeyFailure.Error);
        }
    }

    private AuthenticateResult AuthenticateAdmin(string adminKey)
    {
        var expected = Convert.FromHexString(blocks.Value.AdminKeyHash!);
        var actual = Convert.FromHexString(KeyHasher.HashAdminKey(adminKey));

        if (!KeyHasher.Matches(expected, actual))
            return Refuse(ApiKeyFailure.Invalid);

        var identity = new ClaimsIdentity([new Claim(BlocksClaims.Scope, BlocksClaims.AdminScope)], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    private async Task<AuthenticateResult> AuthenticateClientAsync(string keyId, string secret)
    {
        var store = Context.RequestServices.GetRequiredService<IClientKeyStore>();
        var lookup = await cache.GetAsync(keyId, store.FindKeyAsync, Context.RequestAborted);

        var presented = KeyHasher.HashSecret(secret);
        ReadOnlySpan<byte> expected = lookup is null ? UnknownKeyHash : lookup.Key.SecretHash.Span;
        var secretMatches = KeyHasher.Matches(expected, presented);

        if (lookup is null || !secretMatches || lookup.Key.StatusAt(clock.GetUtcNow()) != ApiKeyStatus.Active)
            return Refuse(ApiKeyFailure.Invalid);

        if (lookup.ClientDisabled)
            return Refuse(ApiKeyFailure.ClientDisabled);

        usage.Record(keyId);

        var claims = new List<Claim>
        {
            new(BlocksClaims.ClientId, lookup.Key.ClientId),
            new(BlocksClaims.KeyId, keyId)
        };
        claims.AddRange(lookup.Key.Scopes.Select(scope => new Claim(BlocksClaims.Scope, scope)));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    private AuthenticateResult Refuse(ApiKeyFailure failure)
    {
        Context.Items[FailureKey] = failure;

        // Below Warning on purpose: a scan of invented keys must not flood the logs, and the value sent
        // is never part of the message.
        AuthLog.Refused(Logger, failure);
        return AuthenticateResult.Fail(failure.ToString());
    }

    /// <summary>The caller is not authenticated: answer from the recorded reason.</summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        BlocksProblems.WriteAsync(Context, RecordedFailure() switch
        {
            ApiKeyFailure.Invalid => BlocksProblems.InvalidKey(),
            ApiKeyFailure.ClientDisabled => BlocksProblems.ClientDisabled(),
            ApiKeyFailure.StorageUnavailable => BlocksProblems.StorageUnavailable(),
            ApiKeyFailure.Error => BlocksProblems.Unexpected(),
            _ => BlocksProblems.AuthRequired()
        });

    /// <summary>The caller is authenticated but lacks the permission the endpoint needs.</summary>
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        BlocksProblems.WriteAsync(Context, BlocksProblems.ScopeMissing());

    private ApiKeyFailure? RecordedFailure() =>
        Context.Items.TryGetValue(FailureKey, out var failure) ? failure as ApiKeyFailure? : null;
}

/// <summary>Source-generated log methods for authentication. None of them ever takes a key.</summary>
internal static partial class AuthLog
{
    [LoggerMessage(EventId = 9000, Level = LogLevel.Debug, Message = "API key refused: {Failure}")]
    public static partial void Refused(ILogger logger, ApiKeyFailure failure);

    [LoggerMessage(EventId = 9001, Level = LogLevel.Warning,
        Message = "API key {KeyId} could not be checked: storage is unavailable")]
    public static partial void StorageUnavailable(ILogger logger, string? keyId);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Error, Message = "API key {KeyId} could not be checked")]
    public static partial void CheckFailed(ILogger logger, string? keyId, Exception exception);

    [LoggerMessage(EventId = 9003, Level = LogLevel.Warning,
        Message = "Last-used times of {KeyCount} key(s) could not be recorded")]
    public static partial void UsageNotRecorded(ILogger logger, int keyCount, Exception exception);
}
