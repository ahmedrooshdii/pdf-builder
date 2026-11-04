using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Problems;
using Microsoft.AspNetCore.Http;

namespace Blocks.ServiceDefaults.Sessions;

/// <summary>
/// The CSRF defence for cookie-authenticated changes: a request authenticated by a console session
/// whose method changes state must carry <c>X-Console: 1</c>. A cross-site form cannot set a custom
/// header, and a cross-origin <c>fetch</c> with one needs a CORS approval the API never gives.
/// Requests authenticated with <c>X-Api-Key</c> are never affected. Runs after authentication.
/// </summary>
internal sealed class ConsoleHeaderMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.AuthenticationType == ConsoleSessionDefaults.Scheme
            && !IsSafe(context.Request.Method)
            && !HasConsoleHeader(context.Request))
            return BlocksProblems.WriteAsync(context, BlocksProblems.ConsoleHeaderRequired());

        return next(context);
    }

    /// <summary>Exactly one <c>X-Console: 1</c>.</summary>
    internal static bool HasConsoleHeader(HttpRequest request) =>
        request.Headers.TryGetValue(ConsoleSessionDefaults.HeaderName, out var values)
        && values.Count == 1
        && values[0] == ConsoleSessionDefaults.HeaderValue;

    private static bool IsSafe(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
}
