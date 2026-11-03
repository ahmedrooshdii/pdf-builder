using System.Globalization;
using System.Text.RegularExpressions;

namespace PdfBuilder.Domain.Templates;

/// <summary>
/// The rules for paper sizes and page lengths, kept in one place because the template editor, the
/// HTTP endpoint and the message consumer all enforce them.
/// </summary>
public static partial class PageSize
{
    /// <summary>The named paper sizes Chromium knows. Matched case-insensitively.</summary>
    public static IReadOnlyList<string> Formats { get; } =
        ["A0", "A1", "A2", "A3", "A4", "A5", "A6", "Letter", "Legal", "Tabloid", "Ledger"];

    public const string FormatRule = "one of A0–A6, Letter, Legal, Tabloid or Ledger";

    public const string LengthRule = "a number with a unit: mm, cm, in or px (for example 80mm)";

    /// <summary>Generous enough for a banner, small enough that a typo cannot ask Chromium for a kilometre of paper.</summary>
    private const double MaxMillimetres = 5_000;

    public static bool IsKnownFormat(string? format) =>
        format is not null && Formats.Contains(format, StringComparer.OrdinalIgnoreCase);

    /// <summary>A length that may be zero: a margin.</summary>
    public static bool IsValidLength(string? value) => TryMillimetres(value, out var mm) && mm <= MaxMillimetres;

    /// <summary>A length that must be positive: a page width or height.</summary>
    public static bool IsValidPageLength(string? value) => TryMillimetres(value, out var mm) && mm is > 0 and <= MaxMillimetres;

    /// <summary>
    /// A page height of <c>auto</c> makes the page exactly as tall as its content: one long page,
    /// the way a till prints a receipt off a roll.
    /// </summary>
    public const string Auto = "auto";

    public const string HeightRule = $"{LengthRule}, or '{Auto}' to fit the content";

    public static bool IsAuto(string? value) => string.Equals(value, Auto, StringComparison.OrdinalIgnoreCase);

    public static bool IsValidPageHeight(string? value) => IsAuto(value) || IsValidPageLength(value);

    /// <summary>The longest page there can be, in CSS pixels. An <c>auto</c> height is capped here.</summary>
    public static double MaxPixels => MaxMillimetres / 25.4 * 96;

    /// <summary>Converts a valid length to CSS pixels (96 to the inch), the unit the browser lays out in.</summary>
    /// <exception cref="ArgumentException"><paramref name="length"/> is not a valid length.</exception>
    public static double ToPixels(string length) => TryMillimetres(length, out var mm)
        ? mm / 25.4 * 96
        : throw new ArgumentException($"'{length}' is not {LengthRule}.", nameof(length));

    private static bool TryMillimetres(string? value, out double millimetres)
    {
        millimetres = 0;
        if (value is null)
            return false;

        var match = Length().Match(value);
        if (!match.Success)
            return false;

        var number = double.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);
        millimetres = match.Groups["unit"].Value switch
        {
            "mm" => number,
            "cm" => number * 10,
            "in" => number * 25.4,
            _ => number * 25.4 / 96   // px, at the CSS 96 per inch
        };
        return true;
    }

    [GeneratedRegex(@"^(?<number>\d{1,5}(\.\d{1,4})?)(?<unit>mm|cm|in|px)$")]
    private static partial Regex Length();
}
