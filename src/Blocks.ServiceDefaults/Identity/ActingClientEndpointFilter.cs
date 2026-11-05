using System.Globalization;
using Blocks.ServiceDefaults.Clients;
using Blocks.ServiceDefaults.Problems;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blocks.ServiceDefaults.Identity;

/// <summary>How a block says that an endpoint is about one client's data.</summary>
public static class ActingClientExtensions
{
    /// <summary>
    /// The endpoint, or every endpoint of the group, works on one client's data. A caller with a client key is
    /// that client and passes. The operator must have chosen a client (<c>X-Client-Id</c>): with none the call is
    /// refused <c>400 VALIDATION_FAILED</c>, and with one that is not registered, <c>404 CLIENT_NOT_FOUND</c>. A
    /// disabled client is fine: the operator can still look at its data and prepare it. Checked after
    /// authentication and authorization, before the handler.
    /// </summary>
    public static TBuilder RequireActingClient<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter<TBuilder, ActingClientEndpointFilter>();
}

/// <summary>See <see cref="ActingClientExtensions.RequireActingClient{TBuilder}"/>.</summary>
internal sealed class ActingClientEndpointFilter(ICurrentClient caller, IClientKeyStore clients, ILogger<ActingClientEndpointFilter> logger)
    : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // A key's client exists and is enabled: authentication already refused it otherwise. Only the operator
        // names a client by hand, so only the operator's choice is checked, and a client call costs no lookup.
        if (!caller.IsAdmin)
            return await next(context);

        if (caller.ClientId is not { } clientId)
            return BlocksProblems.Result(BlocksProblems.ClientRequired());

        try
        {
            if (await clients.GetClientAsync(clientId, context.HttpContext.RequestAborted) is null)
                return BlocksProblems.Result(BlocksProblems.ClientNotFound());
        }
        catch (StorageUnavailableException exception)
        {
            // Never "no such client" for an outage: the console would tell the operator their client is gone.
            ActingClientLog.StorageUnavailable(logger, exception);
            context.HttpContext.Response.Headers.RetryAfter =
                BlocksProblems.StorageRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return BlocksProblems.Result(BlocksProblems.StorageUnavailable());
        }

        return await next(context);
    }
}

internal static partial class ActingClientLog
{
    [LoggerMessage(EventId = 9110, Level = LogLevel.Warning, Message = "Could not check the client the operator chose: storage is unavailable")]
    public static partial void StorageUnavailable(ILogger logger, Exception exception);
}
