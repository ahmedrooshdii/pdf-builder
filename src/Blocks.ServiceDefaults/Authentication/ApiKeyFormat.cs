using System.Buffers;
using System.Security.Cryptography;

namespace Blocks.ServiceDefaults.Authentication;

/// <summary>What a value sent in <c>X-Api-Key</c> turned out to be.</summary>
internal enum ApiKeyKind
{
    Malformed,
    Admin,
    Client
}

/// <summary>A parsed <c>X-Api-Key</c> value. <see cref="KeyId"/> and <see cref="Secret"/> are set for client keys only.</summary>
internal readonly record struct ParsedApiKey(ApiKeyKind Kind, string? KeyId = null, string? Secret = null)
{
    public static ParsedApiKey Malformed { get; } = new(ApiKeyKind.Malformed);
}

/// <summary>
/// The shape of keys: <c>{block}_{keyId}_{secret}</c> for a client, <c>adm_{secret}</c> for the operator.
/// </summary>
/// <remarks>
/// The prefix tells a leaked key's owner where to revoke it and lets secret scanners recognise it.
/// The key id is public (lists and logs show it) and is the primary key of the stored row; only the
/// secret is hashed. Parsing never throws and never does work proportional to an oversized value.
/// </remarks>
public static class ApiKeyFormat
{
    public const string AdminPrefix = "adm";
    public const int KeyIdLength = 8;
    public const int SecretLength = 40;

    /// <summary>No key this service issues is longer; anything longer is refused before it is looked at.</summary>
    public const int MaxLength = 64;

    private const string KeyIdAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    private static readonly SearchValues<char> KeyIdChars = SearchValues.Create(KeyIdAlphabet);
    private const string SecretAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private static readonly SearchValues<char> SecretChars = SearchValues.Create(SecretAlphabet);

    /// <summary>A block code is 2–10 lowercase letters, and never the admin prefix.</summary>
    public static bool IsValidBlockCode(string? blockCode) =>
        blockCode is { Length: >= 2 and <= 10 }
        && blockCode.All(char.IsAsciiLetterLower)
        && blockCode != AdminPrefix;

    /// <summary>A new public key id: 8 characters from <c>[a-z0-9]</c>.</summary>
    public static string NewKeyId() => RandomNumberGenerator.GetString(KeyIdAlphabet, KeyIdLength);

    /// <summary>A new secret: 40 base62 characters, about 238 bits.</summary>
    public static string NewSecret() => RandomNumberGenerator.GetString(SecretAlphabet, SecretLength);

    /// <summary>The full client key, shown once when it is issued.</summary>
    public static string ClientKey(string blockCode, string keyId, string secret) => $"{blockCode}_{keyId}_{secret}";

    /// <summary>A new admin key: <c>adm_</c> and a 40-character secret.</summary>
    public static string NewAdminKey() => $"{AdminPrefix}_{NewSecret()}";

    /// <summary>How lists show a key: enough to recognise it, nothing that can be used.</summary>
    public static string Display(string blockCode, string keyId) => $"{blockCode}_{keyId}_…";

    internal static ParsedApiKey Parse(string? value, string blockCode)
    {
        // Length first: an oversized value costs nothing more than this comparison.
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
            return ParsedApiKey.Malformed;

        var adminLength = AdminPrefix.Length + 1 + SecretLength;
        if (value.Length == adminLength
            && value.StartsWith(AdminPrefix + "_", StringComparison.Ordinal)
            && IsSecret(value.AsSpan(AdminPrefix.Length + 1)))
            return new ParsedApiKey(ApiKeyKind.Admin);

        var clientLength = blockCode.Length + 1 + KeyIdLength + 1 + SecretLength;
        if (value.Length != clientLength
            || !value.StartsWith(blockCode + "_", StringComparison.Ordinal)
            || value[blockCode.Length + 1 + KeyIdLength] != '_')
            return ParsedApiKey.Malformed;

        var keyId = value.Substring(blockCode.Length + 1, KeyIdLength);
        var secret = value[(blockCode.Length + 1 + KeyIdLength + 1)..];

        return IsKeyId(keyId) && IsSecret(secret)
            ? new ParsedApiKey(ApiKeyKind.Client, keyId, secret)
            : ParsedApiKey.Malformed;
    }

    internal static bool IsKeyId(ReadOnlySpan<char> value) =>
        value.Length == KeyIdLength && !value.ContainsAnyExcept(KeyIdChars);

    private static bool IsSecret(ReadOnlySpan<char> value) =>
        value.Length == SecretLength && !value.ContainsAnyExcept(SecretChars);
}
