using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Blocks.ServiceDefaults.Identity;

/// <summary>
/// Puts the caller on every log line of the rest of the request: <c>ClientId</c> and <c>KeyId</c>
/// for a client key, <c>Admin = true</c> for the admin key. Runs after authentication.
/// </summary>
internal sealed class ClientLogScopeMiddleware(RequestDelegate next, ILogger<ClientLogScopeMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ICurrentClient caller)
    {
        var scope = Describe(caller);
        if (scope is null)
        {
            await next(context);
            return;
        }

        using (logger.BeginScope(scope))
            await next(context);
    }

    private static Dictionary<string, object>? Describe(ICurrentClient caller)
    {
        if (caller.IsAdmin)
        {
            var scope = new Dictionary<string, object> { ["Admin"] = true };
            if (caller.ClientId is not null)
                scope["ClientId"] = caller.ClientId;
            return scope;
        }

        if (caller.ClientId is null)
            return null;

        return new Dictionary<string, object>
        {
            ["ClientId"] = caller.ClientId,
            ["KeyId"] = caller.KeyId ?? string.Empty
        };
    }
}
