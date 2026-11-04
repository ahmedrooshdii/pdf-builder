using System.Security.Claims;
using System.Text.RegularExpressions;
using Blocks.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Http;

namespace Blocks.ServiceDefaults.Identity;

/// <summary><see cref="ICurrentClient"/> for an HTTP request, from the authenticated principal.</summary>
/// <remarks>
/// The one place below the handlers that looks at a header: <c>X-Client-Id</c>, honoured only for the
/// operator's authority (constitution Principle VI), so the console can work inside a chosen client.
/// A malformed value from the operator never gets this far: <see cref="ActingClientMiddleware"/> refuses it.
/// </remarks>
internal sealed partial class HttpCurrentClient(IHttpContextAccessor accessor) : ICurrentClient
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public string? ClientId =>
        User?.FindFirstValue(BlocksClaims.ClientId)
        ?? (IsAdmin ? ActingClientId(accessor.HttpContext!.Request) : null);

    public string? KeyId => User?.FindFirstValue(BlocksClaims.KeyId);

    public IReadOnlySet<string> Scopes =>
        User?.FindAll(BlocksClaims.Scope).Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal)
        ?? new HashSet<string>(StringComparer.Ordinal);

    public bool IsAdmin => User?.HasClaim(BlocksClaims.Scope, BlocksClaims.AdminScope) ?? false;

    /// <summary>The one well-formed <c>X-Client-Id</c> value, or null.</summary>
    internal static string? ActingClientId(HttpRequest request) =>
        request.Headers.TryGetValue(ConsoleSessionDefaults.ClientIdHeaderName, out var values)
        && values.Count == 1
        && IsClientId(values[0])
            ? values[0]
            : null;

    /// <summary>The client id rule of the management API: <c>^[a-z][a-z0-9-]{0,39}$</c>.</summary>
    internal static bool IsClientId(string? value) => value is not null && ClientIdPattern().IsMatch(value);

    [GeneratedRegex("^[a-z][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex ClientIdPattern();
}
