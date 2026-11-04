using System.Globalization;
using System.Net;
using Blocks.ServiceDefaults.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Blocks.ServiceDefaults;

/// <summary>
/// What the authentication stud needs to know about the block that uses it. The block code and the
/// permissions come from the block's code; the admin key hash and the console settings come from
/// configuration.
/// </summary>
public sealed class BlocksAuthOptions
{
    /// <summary>The configuration key of the admin key hash, quoted in start-up errors.</summary>
    public const string AdminKeyHashKey = "Blocks:Admin:KeyHash";

    /// <summary>How long a console sign-in lasts, from sign-in, never extended by use.</summary>
    public const string ConsoleSessionLifetimeKey = "Blocks:Console:SessionLifetime";

    /// <summary>Comma-separated CIDRs of reverse proxies whose forwarded headers are believed.</summary>
    public const string KnownNetworksKey = "Blocks:ForwardedHeaders:KnownNetworks";

    /// <summary>"A working day": the default console session lifetime.</summary>
    public static readonly TimeSpan DefaultConsoleSessionLifetime = TimeSpan.FromHours(12);

    internal static readonly TimeSpan MinConsoleSessionLifetime = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan MaxConsoleSessionLifetime = TimeSpan.FromHours(24);

    /// <summary>The prefix of this block's keys, for example <c>pdf</c> in <c>pdf_k7f2a9c1_…</c>.</summary>
    public string BlockCode { get; set; } = string.Empty;

    /// <summary>Every permission a client key of this block can hold, for example <c>pdf:generate</c>.</summary>
    public IReadOnlyList<string> Permissions { get; set; } = [];

    /// <summary>The same permissions with the one-line description the console shows, in declaration order.</summary>
    public IReadOnlyList<PermissionDescriptor> PermissionDescriptors { get; set; } = [];

    /// <summary>
    /// Lowercase hex SHA-256 of the admin key, as printed by <c>Blocks.Cli admin-key</c>. Required:
    /// there is no setting that turns authentication off.
    /// </summary>
    public string? AdminKeyHash { get; set; }

    /// <summary>How long a console session lasts. Default 12 hours; 5 minutes to 24 hours.</summary>
    public TimeSpan ConsoleSessionLifetime { get; set; } = DefaultConsoleSessionLifetime;

    /// <summary>Reverse proxies whose <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> are believed. Empty: loopback only.</summary>
    public IReadOnlyList<IPNetwork> KnownNetworks { get; set; } = [];

    /// <summary>Settings that could not be read at all, reported by the validator with the key that holds them.</summary>
    internal List<string> UnreadableSettings { get; } = [];

    /// <summary>Reads the console settings from configuration, recording what cannot be parsed.</summary>
    internal void ReadConsoleSettings(IConfiguration configuration)
    {
        var lifetime = configuration[ConsoleSessionLifetimeKey];
        if (!string.IsNullOrWhiteSpace(lifetime))
        {
            if (TimeSpan.TryParse(lifetime, CultureInfo.InvariantCulture, out var parsed))
                ConsoleSessionLifetime = parsed;
            else
                UnreadableSettings.Add($"{ConsoleSessionLifetimeKey} must be a time span such as 12:00:00, not '{lifetime}'.");
        }

        var networks = configuration[KnownNetworksKey];
        if (string.IsNullOrWhiteSpace(networks))
            return;

        var parsedNetworks = new List<IPNetwork>();
        foreach (var entry in networks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPNetwork.TryParse(entry, out var network))
                parsedNetworks.Add(network);
            else
                UnreadableSettings.Add($"{KnownNetworksKey} must be comma-separated CIDRs such as 172.16.0.0/12; '{entry}' is not one.");
        }

        KnownNetworks = parsedNetworks;
    }
}

/// <summary>Refuses to start with a missing or malformed setting, naming it.</summary>
internal sealed class BlocksAuthOptionsValidator : IValidateOptions<BlocksAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, BlocksAuthOptions options)
    {
        var errors = new List<string>(options.UnreadableSettings);

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

        if (options.ConsoleSessionLifetime < BlocksAuthOptions.MinConsoleSessionLifetime
            || options.ConsoleSessionLifetime > BlocksAuthOptions.MaxConsoleSessionLifetime)
            errors.Add($"{BlocksAuthOptions.ConsoleSessionLifetimeKey} must be between 00:05:00 and 24:00:00.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static bool IsLowercaseSha256Hex(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
