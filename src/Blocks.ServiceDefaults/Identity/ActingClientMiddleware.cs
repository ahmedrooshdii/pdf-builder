using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Problems;
using Microsoft.AspNetCore.Http;

namespace Blocks.ServiceDefaults.Identity;

/// <summary>
/// Refuses an <c>X-Client-Id</c> from the operator that is not one client id: sent twice, or not
/// matching the client id rule. Callers with a client key are not affected; their header is ignored.
/// Whether the client exists is left to client-scoped data, which answers 404 for an unknown one.
/// </summary>
internal sealed class ActingClientMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.User.HasClaim(BlocksClaims.Scope, BlocksClaims.AdminScope)
            && context.Request.Headers.ContainsKey(ConsoleSessionDefaults.ClientIdHeaderName)
            && HttpCurrentClient.ActingClientId(context.Request) is null)
        {
            return BlocksProblems.WriteAsync(context, BlocksProblems.Validation(new Dictionary<string, string[]>
            {
                [ConsoleSessionDefaults.ClientIdHeaderName] =
                [
                    "Must be a client id: lowercase letters, digits and hyphens, starting with a letter, up to 40 characters."
                ]
            }));
        }

        return next(context);
    }
}
