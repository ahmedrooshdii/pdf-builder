using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Blocks.ServiceDefaults.Files;

/// <summary>A query string that makes a file link work until <paramref name="ExpiresAt"/>.</summary>
public readonly record struct SignedFileQuery(string Query, DateTimeOffset ExpiresAt);

/// <summary>What a request's query says about the file it asks for.</summary>
public enum FileLinkCheck
{
    /// <summary>Neither <c>expires</c> nor <c>sig</c> is present: an unsigned link.</summary>
    NoSignature,

    /// <summary>Signed for this file, and not yet expired.</summary>
    Valid,

    /// <summary>Anything else: altered, expired, for another file, malformed, or only half there.</summary>
    Invalid
}

/// <summary>
/// Makes and checks signed file links: <c>?expires={unix seconds}&amp;sig={base64url(HMAC-SHA256)}</c> over
/// <c>"{fileId}.{expires}"</c>. Holds the key privately; nothing here logs or prints it.
/// </summary>
/// <remarks>
/// Several instances of a block share one key, so a link made by one is accepted by the others.
/// </remarks>
public sealed class FileLinkSigner
{
    private const string ExpiresParameter = "expires";
    private const string SignatureParameter = "sig";
    private const int MaxExpiresDigits = 12;
    private const int SignatureLength = 43;   // 32 bytes, base64url, no padding

    private readonly byte[] _key;
    private readonly TimeSpan _lifetime;

    public FileLinkSigner(IOptions<BlocksFilesOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;
        _key = Convert.FromBase64String(value.SigningKey!.Trim());
        _lifetime = value.UrlLifetime;
    }

    /// <summary>Signs a link for <paramref name="fileId"/> that works from now until the configured lifetime has passed.</summary>
    public SignedFileQuery Sign(string fileId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileId);

        var until = now + _lifetime;
        var seconds = until.ToUnixTimeSeconds();
        if (until.UtcTicks % TimeSpan.TicksPerSecond != 0)
            seconds++;      // round up: a link never lives shorter than asked

        var signature = Compute(fileId, seconds);
        return new SignedFileQuery(
            $"{ExpiresParameter}={seconds.ToString(CultureInfo.InvariantCulture)}&{SignatureParameter}={signature}",
            DateTimeOffset.FromUnixTimeSeconds(seconds));
    }

    /// <summary>Whether <paramref name="query"/> is a valid, unexpired signature for <paramref name="fileId"/>.</summary>
    public FileLinkCheck Check(string fileId, IQueryCollection query, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(query);

        var hasExpires = query.TryGetValue(ExpiresParameter, out var expires);
        var hasSignature = query.TryGetValue(SignatureParameter, out var signature);

        if (!hasExpires && !hasSignature)
            return FileLinkCheck.NoSignature;

        // Half a signature, or a parameter given twice, is not a signature.
        if (expires.Count != 1 || signature.Count != 1)
            return FileLinkCheck.Invalid;

        var expiresText = expires[0];
        var signatureText = signature[0];
        if (!IsDigits(expiresText) || !IsBase64Url(signatureText))
            return FileLinkCheck.Invalid;

        var seconds = long.Parse(expiresText!, NumberStyles.None, CultureInfo.InvariantCulture);

        // Both are always checked, so the answer does not depend on which part was wrong.
        var expected = Encoding.ASCII.GetBytes(Compute(fileId, seconds));
        var matches = CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(signatureText!));
        var live = now.ToUnixTimeSeconds() < seconds;

        return matches && live ? FileLinkCheck.Valid : FileLinkCheck.Invalid;
    }

    private string Compute(string fileId, long expires)
    {
        var message = Encoding.UTF8.GetBytes($"{fileId}.{expires.ToString(CultureInfo.InvariantCulture)}");
        return Base64Url.EncodeToString(HMACSHA256.HashData(_key, message));
    }

    private static bool IsDigits(string? value) =>
        value is { Length: > 0 and <= MaxExpiresDigits } && value.All(char.IsAsciiDigit);

    private static bool IsBase64Url(string? value) =>
        value is { Length: SignatureLength } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
