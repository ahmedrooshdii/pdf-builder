using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Problems;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.ServiceDefaults.Clients;

/// <summary>
/// <c>/api/admin/clients…</c>: registering and disabling clients, issuing, listing and revoking keys.
/// Every block maps the same operations, and every one of them needs the admin key.
/// </summary>
internal static class ClientAdminEndpoints
{
    // A key id is 8 characters from 36; drawing a used one five times running does not happen.
    private const int MaxKeyIdAttempts = 5;

    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/clients")
            .WithTags("Admin")
            .RequirePermission(BlocksClaims.AdminScope)
            .AddEndpointFilter<StorageUnavailableFilter>();

        group.MapPost("/", CreateClientAsync)
            .WithName("CreateClient")
            .WithSummary("Register a client")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", ListClientsAsync)
            .WithName("ListClients")
            .WithSummary("List clients, ordered by id");

        group.MapGet("/{clientId}", GetClientAsync)
            .WithName("GetClient")
            .WithSummary("Read a client")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{clientId}", UpdateClientAsync)
            .WithName("UpdateClient")
            .WithSummary("Rename, disable or enable a client")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{clientId}/keys", IssueKeyAsync)
            .WithName("IssueKey")
            .WithSummary("Issue a key; the response is the only time it is shown")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{clientId}/keys", ListKeysAsync)
            .WithName("ListKeys")
            .WithSummary("List a client's keys, newest first, never the keys themselves")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{clientId}/keys/{keyId}", RevokeKeyAsync)
            .WithName("RevokeKey")
            .WithSummary("Revoke a key, permanently")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> CreateClientAsync(
        CreateClientRequest request, IClientKeyStore store, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        var errors = ClientAdminValidation.ValidateCreate(request);
        if (errors.Count != 0)
            return BlocksProblems.Result(BlocksProblems.Validation(errors));

        var client = new ClientInfo(request.Id!, request.Name!.Trim(), clock.GetUtcNow(), null);
        if (!await store.AddClientAsync(client, ct))
            return BlocksProblems.Result(BlocksProblems.ClientIdTaken());

        var logger = loggers.CreateLogger(LogCategory);
        AdminLog.ClientRegistered(logger, client.Id, client.Name);
        return TypedResults.Created($"/api/admin/clients/{client.Id}", ClientResponse.From(client));
    }

    private static async Task<Ok<ListResponse<ClientResponse>>> ListClientsAsync(IClientKeyStore store, CancellationToken ct) =>
        TypedResults.Ok(new ListResponse<ClientResponse>(
            (await store.ListClientsAsync(ct)).Select(ClientResponse.From).ToList()));

    private static async Task<IResult> GetClientAsync(string clientId, IClientKeyStore store, CancellationToken ct) =>
        await store.GetClientAsync(clientId, ct) is { } client
            ? TypedResults.Ok(ClientResponse.From(client))
            : BlocksProblems.Result(BlocksProblems.ClientNotFound());

    private static async Task<IResult> UpdateClientAsync(
        string clientId,
        UpdateClientRequest request,
        IClientKeyStore store,
        KeyCache cache,
        TimeProvider clock,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var errors = ClientAdminValidation.ValidateUpdate(request);
        if (errors.Count != 0)
            return BlocksProblems.Result(BlocksProblems.Validation(errors));

        var before = await store.GetClientAsync(clientId, ct);
        if (before is null)
            return BlocksProblems.Result(BlocksProblems.ClientNotFound());

        var after = await store.UpdateClientAsync(clientId, request.Name?.Trim(), request.Disabled, clock.GetUtcNow(), ct);
        if (after is null)
            return BlocksProblems.Result(BlocksProblems.ClientNotFound());

        if (before.IsDisabled != after.IsDisabled)
        {
            // This instance stops (or resumes) accepting the client's keys at once; the others within a minute.
            foreach (var key in await store.ListKeysAsync(clientId, ct))
                cache.Forget(key.KeyId);

            var logger = loggers.CreateLogger(LogCategory);
            var state = after.IsDisabled ? "disabled" : "enabled";
            AdminLog.ClientStateChanged(logger, clientId, state);
        }

        return TypedResults.Ok(ClientResponse.From(after));
    }

    private static async Task<IResult> IssueKeyAsync(
        string clientId,
        IssueKeyRequest request,
        HttpContext http,
        IClientKeyStore store,
        IOptions<BlocksAuthOptions> options,
        TimeProvider clock,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        if (await store.GetClientAsync(clientId, ct) is null)
            return BlocksProblems.Result(BlocksProblems.ClientNotFound());

        var now = clock.GetUtcNow();
        var blocks = options.Value;

        var errors = ClientAdminValidation.ValidateIssue(request, blocks.Permissions, now);
        if (errors.Count != 0)
            return BlocksProblems.Result(BlocksProblems.Validation(errors));

        var scopes = request.Scopes!.Distinct(StringComparer.Ordinal).ToList();

        for (var attempt = 0; attempt < MaxKeyIdAttempts; attempt++)
        {
            var keyId = ApiKeyFormat.NewKeyId();
            var secret = ApiKeyFormat.NewSecret();

            var key = new ApiKeyInfo(
                keyId, clientId, request.Name!.Trim(), scopes, KeyHasher.HashSecret(secret),
                now, request.ExpiresAt?.ToUniversalTime(), RevokedAt: null, LastUsedAt: null);

            if (!await store.AddKeyAsync(key, ct))
                continue;

            var logger = loggers.CreateLogger(LogCategory);
            AdminLog.KeyIssued(logger, clientId, keyId, key.Name, scopes);

            // The only response that ever carries the key: no cache anywhere may keep it.
            http.Response.Headers.CacheControl = "no-store";
            var view = KeyResponse.From(key, blocks.BlockCode, now);
            return TypedResults.Created(
                (string?)null,
                IssuedKeyResponse.From(ApiKeyFormat.ClientKey(blocks.BlockCode, keyId, secret), view));
        }

        throw new InvalidOperationException($"No unused key id was found in {MaxKeyIdAttempts} attempts.");
    }

    private static async Task<IResult> ListKeysAsync(
        string clientId, IClientKeyStore store, IOptions<BlocksAuthOptions> options, TimeProvider clock, CancellationToken ct)
    {
        if (await store.GetClientAsync(clientId, ct) is null)
            return BlocksProblems.Result(BlocksProblems.ClientNotFound());

        var now = clock.GetUtcNow();
        var keys = await store.ListKeysAsync(clientId, ct);

        return TypedResults.Ok(new ListResponse<KeyResponse>(
            keys.Select(key => KeyResponse.From(key, options.Value.BlockCode, now)).ToList()));
    }

    private static async Task<IResult> RevokeKeyAsync(
        string clientId,
        string keyId,
        IClientKeyStore store,
        KeyCache cache,
        TimeProvider clock,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var key = await store.RevokeKeyAsync(clientId, keyId, clock.GetUtcNow(), ct);
        if (key is null)
            return BlocksProblems.Result(BlocksProblems.KeyNotFound());

        cache.Forget(keyId);
        var logger = loggers.CreateLogger(LogCategory);
        AdminLog.KeyRevoked(logger, clientId, keyId, key.Name);

        return TypedResults.NoContent();
    }

    private const string LogCategory = "Blocks.ServiceDefaults.Clients.ClientAdmin";
}

/// <summary>Answers <c>503 STORAGE_UNAVAILABLE</c> for any management call made while the database is down.</summary>
internal sealed class StorageUnavailableFilter(ILogger<StorageUnavailableFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (StorageUnavailableException exception)
        {
            AdminLog.StorageUnavailable(logger, exception);
            context.HttpContext.Response.Headers.RetryAfter =
                BlocksProblems.StorageRetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return BlocksProblems.Result(BlocksProblems.StorageUnavailable());
        }
    }
}

/// <summary>What the operator did, for the audit trail in the logs. Never the key.</summary>
internal static partial class AdminLog
{
    [LoggerMessage(EventId = 9100, Level = LogLevel.Information, Message = "Client {ClientId} registered as \"{ClientName}\"")]
    public static partial void ClientRegistered(ILogger logger, string clientId, string clientName);

    [LoggerMessage(EventId = 9101, Level = LogLevel.Information, Message = "Client {ClientId} {State}")]
    public static partial void ClientStateChanged(ILogger logger, string clientId, string state);

    [LoggerMessage(EventId = 9102, Level = LogLevel.Information,
        Message = "Key {KeyId} (\"{KeyName}\") issued to client {ClientId} with permissions {Scopes}")]
    public static partial void KeyIssued(ILogger logger, string clientId, string keyId, string keyName, IReadOnlyList<string> scopes);

    [LoggerMessage(EventId = 9103, Level = LogLevel.Information, Message = "Key {KeyId} (\"{KeyName}\") of client {ClientId} revoked")]
    public static partial void KeyRevoked(ILogger logger, string clientId, string keyId, string keyName);

    [LoggerMessage(EventId = 9104, Level = LogLevel.Warning, Message = "Management request refused: storage is unavailable")]
    public static partial void StorageUnavailable(ILogger logger, Exception exception);
}
