namespace Blocks.ServiceDefaults.Authentication;

/// <summary>The claims an authenticated caller carries, whatever way it signed in.</summary>
public static class BlocksClaims
{
    /// <summary>The client the key belongs to. Absent for the admin key.</summary>
    public const string ClientId = "client_id";

    /// <summary>The public id of the key. Absent for the admin key.</summary>
    public const string KeyId = "key_id";

    /// <summary>One claim per permission the caller holds.</summary>
    public const string Scope = "scope";

    /// <summary>The operator's authority: satisfies every permission. Held only by the admin key.</summary>
    public const string AdminScope = "admin";
}

/// <summary>The one authentication scheme of this release, reading <c>X-Api-Key</c>.</summary>
public static class ApiKeyAuthenticationDefaults
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
}
