using System.Security.Cryptography;
using System.Text;

namespace Blocks.ServiceDefaults.Authentication;

/// <summary>
/// SHA-256 for keys, and constant-time comparison of the results.
/// </summary>
/// <remarks>
/// A fast hash is the right tool here: the secrets are 238 random bits, so there is nothing for a
/// slow hash to protect against, and a slow one would be paid on every request.
/// </remarks>
public static class KeyHasher
{
    /// <summary>The stored form of a client key's secret: 32 bytes.</summary>
    public static byte[] HashSecret(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    /// <summary>The configured form of the admin key (<c>Blocks:Admin:KeyHash</c>): lowercase hex of the whole key.</summary>
    public static string HashAdminKey(string adminKey)
    {
        ArgumentNullException.ThrowIfNull(adminKey);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(adminKey)));
    }

    /// <summary>Equality whose time does not depend on where the inputs first differ.</summary>
    public static bool Matches(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) =>
        CryptographicOperations.FixedTimeEquals(expected, actual);
}
