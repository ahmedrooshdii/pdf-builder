namespace Blocks.ServiceDefaults.Identity;

/// <summary>
/// Who is calling, read from the principal's claims. Code below authentication reads identity only
/// through this, never from headers, keys or schemes (constitution Principle VI).
/// </summary>
public interface ICurrentClient
{
    /// <summary>
    /// The client behind the key; for the operator (admin key or console session), the client named in
    /// <c>X-Client-Id</c>, or null when the operator is not acting as a client; null for anonymous requests.
    /// </summary>
    string? ClientId { get; }

    /// <summary>The public id of the key; null for the admin key and for anonymous requests.</summary>
    string? KeyId { get; }

    /// <summary>Every permission the caller holds.</summary>
    IReadOnlySet<string> Scopes { get; }

    /// <summary>True for the operator, whose authority satisfies every permission.</summary>
    bool IsAdmin { get; }
}
