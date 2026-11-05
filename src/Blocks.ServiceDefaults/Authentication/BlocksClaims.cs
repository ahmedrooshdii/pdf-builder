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

    /// <summary>The operator's authority: satisfies every permission. Held by the admin key and console sessions.</summary>
    public const string AdminScope = "admin";

    /// <summary>The console session's id, inside the protected cookie only. Stored by the service as a hash.</summary>
    public const string SessionId = "sid";
}

/// <summary>The <c>X-Api-Key</c> scheme, for product keys and the admin key alike.</summary>
public static class ApiKeyAuthenticationDefaults
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
}

/// <summary>The scheme every endpoint authenticates with: it forwards to <c>ApiKey</c> or <c>ConsoleSession</c>.</summary>
public static class BlocksAuthenticationDefaults
{
    public const string PolicyScheme = "Blocks";
}

/// <summary>The operator's console session: a cookie checked against a stored row.</summary>
public static class ConsoleSessionDefaults
{
    public const string Scheme = "ConsoleSession";

    /// <summary>Required on every cookie-authenticated change and on sign-in and sign-out (the CSRF defence).</summary>
    public const string HeaderName = "X-Console";
    public const string HeaderValue = "1";

    /// <summary>Lets the operator act as a client on client-scoped endpoints. Honoured only for the admin authority.</summary>
    public const string ClientIdHeaderName = "X-Client-Id";

    /// <summary>The cookie name, per block, so two blocks on one host keep separate sessions.</summary>
    public static string CookieName(string blockCode) => $"blocks.{blockCode}.console";
}
