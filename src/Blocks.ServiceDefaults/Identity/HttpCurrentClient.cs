using System.Security.Claims;
using Blocks.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Http;

namespace Blocks.ServiceDefaults.Identity;

/// <summary><see cref="ICurrentClient"/> for an HTTP request, from the authenticated principal.</summary>
internal sealed class HttpCurrentClient(IHttpContextAccessor accessor) : ICurrentClient
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public string? ClientId => User?.FindFirstValue(BlocksClaims.ClientId);

    public string? KeyId => User?.FindFirstValue(BlocksClaims.KeyId);

    public IReadOnlySet<string> Scopes =>
        User?.FindAll(BlocksClaims.Scope).Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal)
        ?? new HashSet<string>(StringComparer.Ordinal);

    public bool IsAdmin => User?.HasClaim(BlocksClaims.Scope, BlocksClaims.AdminScope) ?? false;
}
