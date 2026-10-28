using System.Text.RegularExpressions;

namespace PdfBuilder.Domain.Templates;

/// <summary>The rules for a template key, kept in one place because three layers enforce them.</summary>
public static partial class TemplateKey
{
    public const int MaxLength = 64;

    /// <summary>
    /// A key becomes a file name in both template stores, so the character set doubles as a
    /// path-traversal guard: no separators, no dots, nothing a file system treats specially.
    /// </summary>
    public const string Rule = "letters, digits, '_' and '-', starting with a letter or digit (max 64 characters)";

    public static bool IsValid(string? key) => key is not null && Pattern().IsMatch(key);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$")]
    private static partial Regex Pattern();
}
