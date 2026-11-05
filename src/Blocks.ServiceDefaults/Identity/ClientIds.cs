using System.Text.RegularExpressions;

namespace Blocks.ServiceDefaults.Identity;

/// <summary>
/// The rule a client id follows, in one place: a lowercase letter, then up to 39 lowercase letters,
/// digits or hyphens. The management API, <c>X-Client-Id</c> and a block's own messages all use it.
/// </summary>
public static partial class ClientIds
{
    /// <summary>The rule as a regular expression, for documentation and for the database's check constraint.</summary>
    public const string Pattern = "^[a-z][a-z0-9-]{0,39}$";

    /// <summary>True when <paramref name="value"/> is a well-formed client id. Says nothing about whether it is registered.</summary>
    public static bool IsValid(string? value) => value is not null && IdPattern().IsMatch(value);

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
