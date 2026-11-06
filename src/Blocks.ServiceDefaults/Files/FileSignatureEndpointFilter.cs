using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Blocks.ServiceDefaults.Files;

/// <summary>How a block says that the signature on a file link decides whether the file is served.</summary>
public static class FileSignatureExtensions
{
    /// <summary>
    /// The endpoint serves a file named by the route value <c>fileId</c>, and its link is the credential.
    /// A valid signed link passes. An altered, expired or half-present signature is refused with a bare
    /// <c>404</c>, the same answer as for a file that does not exist, so nothing tells a guesser which part was
    /// wrong. An unsigned link passes only while <c>Blocks:Files:RequireSignature</c> is false.
    /// </summary>
    public static TBuilder RequireFileSignature<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter<TBuilder, FileSignatureEndpointFilter>();
}

/// <summary>See <see cref="FileSignatureExtensions.RequireFileSignature{TBuilder}"/>.</summary>
internal sealed class FileSignatureEndpointFilter(
    FileLinkSigner signer, IOptions<BlocksFilesOptions> options, TimeProvider clock) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        var fileId = context.HttpContext.GetRouteValue("fileId") as string ?? string.Empty;

        var check = signer.Check(fileId, request.Query, clock.GetUtcNow());
        var allowed = check == FileLinkCheck.Valid || (check == FileLinkCheck.NoSignature && !options.Value.RequireSignature);

        return allowed ? next(context) : ValueTask.FromResult<object?>(Results.NotFound());
    }
}
