using System.Text.RegularExpressions;
using Blocks.ServiceDefaults.Authentication;

namespace Blocks.ServiceDefaults.Clients;

/// <summary>Body of <c>POST /api/admin/clients</c>.</summary>
internal sealed record CreateClientRequest(string? Id, string? Name);

/// <summary>Body of <c>PATCH /api/admin/clients/{clientId}</c>. Members left out are not changed.</summary>
internal sealed record UpdateClientRequest(string? Name, bool? Disabled);

/// <summary>Body of <c>POST /api/admin/clients/{clientId}/keys</c>.</summary>
internal sealed record IssueKeyRequest(string? Name, IReadOnlyList<string>? Scopes, DateTimeOffset? ExpiresAt);

/// <summary>The stud list envelope. Paging members join it, additively, when shared paging exists.</summary>
internal sealed record ListResponse<T>(IReadOnlyList<T> Items);

internal sealed record ClientResponse(string Id, string Name, string Status, DateTimeOffset CreatedAt, DateTimeOffset? DisabledAt)
{
    public static ClientResponse From(ClientInfo client) => new(
        client.Id,
        client.Name,
        client.IsDisabled ? "Disabled" : "Active",
        client.CreatedAt,
        client.DisabledAt);
}

/// <summary>A key as lists show it: never the key itself.</summary>
internal sealed record KeyResponse(
    string KeyId,
    string Display,
    string Name,
    IReadOnlyList<string> Scopes,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt)
{
    public static KeyResponse From(ApiKeyInfo key, string blockCode, DateTimeOffset now) => new(
        key.KeyId,
        ApiKeyFormat.Display(blockCode, key.KeyId),
        key.Name,
        key.Scopes,
        key.StatusAt(now).ToString(),
        key.CreatedAt,
        key.ExpiresAt,
        key.RevokedAt,
        key.LastUsedAt);
}

/// <summary>The one response that ever contains a key: the one that issues it.</summary>
internal sealed record IssuedKeyResponse(
    string Key,
    string KeyId,
    string Display,
    string Name,
    IReadOnlyList<string> Scopes,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt)
{
    public static IssuedKeyResponse From(string key, KeyResponse view) => new(
        key,
        view.KeyId,
        view.Display,
        view.Name,
        view.Scopes,
        view.Status,
        view.CreatedAt,
        view.ExpiresAt,
        view.RevokedAt,
        view.LastUsedAt);
}

/// <summary>The rules of the management requests. Each method returns field → messages; empty means valid.</summary>
internal static partial class ClientAdminValidation
{
    public const int MaxNameLength = 100;

    [GeneratedRegex("^[a-z][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex ClientIdPattern();

    public static Dictionary<string, string[]> ValidateCreate(CreateClientRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.Id is null || !ClientIdPattern().IsMatch(request.Id))
            errors["id"] = ["The id must be 1 to 40 lowercase letters, digits and hyphens, starting with a letter."];

        AddNameErrors(errors, request.Name, required: true);
        return errors;
    }

    public static Dictionary<string, string[]> ValidateUpdate(UpdateClientRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        AddNameErrors(errors, request.Name, required: false);
        return errors;
    }

    public static Dictionary<string, string[]> ValidateIssue(
        IssueKeyRequest request, IReadOnlyList<string> permissions, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        AddNameErrors(errors, request.Name, required: true);

        var known = string.Join(", ", permissions);
        if (request.Scopes is null || request.Scopes.Count == 0)
        {
            errors["scopes"] = [$"Give the key at least one permission. Known permissions: {known}."];
        }
        else
        {
            // "admin" is not in the list, so it is refused here too: only the admin key holds it.
            var unknown = request.Scopes.Where(scope => !permissions.Contains(scope, StringComparer.Ordinal)).ToList();
            if (unknown.Count != 0)
                errors["scopes"] = [$"Unknown permission(s): {string.Join(", ", unknown)}. Known permissions: {known}."];
        }

        if (request.ExpiresAt is { } expiresAt && expiresAt <= now)
            errors["expiresAt"] = ["The expiry must be in the future, or left out for a key that does not expire."];

        return errors;
    }

    private static void AddNameErrors(Dictionary<string, string[]> errors, string? name, bool required)
    {
        if (name is null && !required)
            return;

        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxNameLength)
            errors["name"] = [$"The name must be 1 to {MaxNameLength} characters."];
    }
}
