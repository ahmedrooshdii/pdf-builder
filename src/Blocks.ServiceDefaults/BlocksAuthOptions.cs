using Blocks.ServiceDefaults.Authentication;
using Microsoft.Extensions.Options;

namespace Blocks.ServiceDefaults;

/// <summary>
/// What the authentication stud needs to know about the block that uses it. The block code and the
/// permissions come from the block's code; the admin key hash comes from configuration.
/// </summary>
public sealed class BlocksAuthOptions
{
    /// <summary>The configuration key of the admin key hash, quoted in start-up errors.</summary>
    public const string AdminKeyHashKey = "Blocks:Admin:KeyHash";

    /// <summary>The prefix of this block's keys, for example <c>pdf</c> in <c>pdf_k7f2a9c1_…</c>.</summary>
    public string BlockCode { get; set; } = string.Empty;

    /// <summary>Every permission a client key of this block can hold, for example <c>pdf:generate</c>.</summary>
    public IReadOnlyList<string> Permissions { get; set; } = [];

    /// <summary>
    /// Lowercase hex SHA-256 of the admin key, as printed by <c>Blocks.Cli admin-key</c>. Required:
    /// there is no setting that turns authentication off.
    /// </summary>
    public string? AdminKeyHash { get; set; }
}

/// <summary>Refuses to start with a missing or malformed admin key hash, naming the setting.</summary>
internal sealed class BlocksAuthOptionsValidator : IValidateOptions<BlocksAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, BlocksAuthOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.AdminKeyHash))
            errors.Add($"{BlocksAuthOptions.AdminKeyHashKey} is required: the hash of the admin key, as printed by " +
                       "`dotnet run --project tools/Blocks.Cli -- admin-key`.");
        else if (!IsLowercaseSha256Hex(options.AdminKeyHash))
            errors.Add($"{BlocksAuthOptions.AdminKeyHashKey} must be 64 lowercase hexadecimal characters (a SHA-256), " +
                       "as printed by `dotnet run --project tools/Blocks.Cli -- admin-key`.");

        if (!ApiKeyFormat.IsValidBlockCode(options.BlockCode))
            errors.Add($"The block code '{options.BlockCode}' must be 2 to 10 lowercase letters, and not 'adm'.");

        if (options.Permissions.Count == 0)
            errors.Add("A block must declare at least one permission.");
        else if (options.Permissions.Contains(BlocksClaims.AdminScope, StringComparer.Ordinal))
            errors.Add($"'{BlocksClaims.AdminScope}' is reserved for the admin key and cannot be a block permission.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static bool IsLowercaseSha256Hex(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
