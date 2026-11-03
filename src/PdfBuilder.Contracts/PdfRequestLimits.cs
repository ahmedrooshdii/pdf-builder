using System.Text;
using System.Text.RegularExpressions;

namespace PdfBuilder.Contracts;

/// <summary>
/// The limits the service enforces on <see cref="IGeneratePdfRequest"/>. A request outside them is
/// answered <see cref="PdfErrorCodes.ValidationFailed"/>. The service validates against these same
/// constants, so checking them before sending saves a round trip without risking drift.
/// </summary>
public static class PdfRequestLimits
{
    /// <summary>Pattern every <see cref="IGeneratePdfRequest.TemplateKey"/> must match: letters, digits, '_' and '-', starting with a letter or digit.</summary>
    public const string TemplateKeyPattern = "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$";

    /// <summary>Longest allowed <see cref="IGeneratePdfRequest.TemplateKey"/>, in characters.</summary>
    public const int TemplateKeyMaxLength = 64;

    /// <summary>
    /// Largest allowed <see cref="IGeneratePdfRequest.JsonPayload"/>, counted in UTF-8 bytes rather
    /// than characters: a payload of Arabic or CJK text reaches it at well under 5 million characters.
    /// </summary>
    public const int MaxPayloadBytes = 5 * 1024 * 1024;

    private static readonly Regex TemplateKeyRegex = new(
        TemplateKeyPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>True when <paramref name="templateKey"/> matches <see cref="TemplateKeyPattern"/>.</summary>
    public static bool IsValidTemplateKey(string? templateKey) =>
        templateKey is not null && TemplateKeyRegex.IsMatch(templateKey);

    /// <summary>True when <paramref name="jsonPayload"/> is within <see cref="MaxPayloadBytes"/>. Does not check that it is a JSON object.</summary>
    public static bool IsWithinPayloadLimit(string? jsonPayload) =>
        jsonPayload is not null && Encoding.UTF8.GetByteCount(jsonPayload) <= MaxPayloadBytes;
}
