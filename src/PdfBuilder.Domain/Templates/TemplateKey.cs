using System.Text.RegularExpressions;
using PdfBuilder.Contracts;

namespace PdfBuilder.Domain.Templates;

/// <summary>The rules for a template key, kept in one place because three layers enforce them.</summary>
public static partial class TemplateKey
{
    public const int MaxLength = PdfRequestLimits.TemplateKeyMaxLength;

    /// <summary>
    /// A key becomes a file name in both template stores, so the character set doubles as a
    /// path-traversal guard: no separators, no dots, nothing a file system treats specially.
    /// </summary>
    public const string Rule = "letters, digits, '_' and '-', starting with a letter or digit (max 64 characters)";

    public static bool IsValid(string? key) => key is not null && Pattern().IsMatch(key);

    // The published pattern, so a caller pre-validating with the contracts package agrees with us.
    [GeneratedRegex(PdfRequestLimits.TemplateKeyPattern)]
    private static partial Regex Pattern();
}
