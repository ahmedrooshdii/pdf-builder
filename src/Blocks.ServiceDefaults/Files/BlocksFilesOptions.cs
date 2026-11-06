using Microsoft.Extensions.Options;

namespace Blocks.ServiceDefaults.Files;

/// <summary>
/// The settings of private, expiring file links, from configuration. The same in every block that serves
/// files; see <c>specs/005-private-expiring-files/contracts/configuration.md</c>.
/// </summary>
public sealed class BlocksFilesOptions
{
    /// <summary>The configuration section of these options.</summary>
    public const string Section = "Blocks:Files";

    /// <summary>The configuration key of <see cref="SigningKey"/>, quoted in start-up errors.</summary>
    public const string SigningKeyKey = "Blocks:Files:SigningKey";

    /// <summary>The configuration key of <see cref="UrlLifetime"/>.</summary>
    public const string UrlLifetimeKey = "Blocks:Files:UrlLifetime";

    /// <summary>The configuration key of <see cref="RetentionDays"/>.</summary>
    public const string RetentionDaysKey = "Blocks:Files:RetentionDays";

    /// <summary>The configuration key of <see cref="RequireSignature"/>.</summary>
    public const string RequireSignatureKey = "Blocks:Files:RequireSignature";

    /// <summary>The shortest a link may live.</summary>
    public static readonly TimeSpan MinUrlLifetime = TimeSpan.FromMinutes(1);

    /// <summary>The longest a link may live.</summary>
    public static readonly TimeSpan MaxUrlLifetime = TimeSpan.FromDays(7);

    /// <summary>Base64 of at least 32 random bytes. A secret. Required.</summary>
    public string? SigningKey { get; set; }

    /// <summary>How long a link issued now keeps working. Default one hour.</summary>
    public TimeSpan UrlLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How many days a file is kept, counted from its creation. Default 30.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// <see langword="true"/>: links are signed and expire, and unsigned links are refused.
    /// <see langword="false"/> (default): links are unsigned and carry no expiry, as before.
    /// </summary>
    public bool RequireSignature { get; set; }
}

/// <summary>Refuses to start with a missing or malformed setting. Messages name the key, never its value.</summary>
internal sealed class BlocksFilesOptionsValidator : IValidateOptions<BlocksFilesOptions>
{
    private const int MinKeyBytes = 32;

    public ValidateOptionsResult Validate(string? name, BlocksFilesOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            failures.Add(
                $"{BlocksFilesOptions.SigningKeyKey} (environment variable Blocks__Files__SigningKey) is required: " +
                "base64 of at least 32 random bytes. Generate one with " +
                "\"dotnet run --project tools/Blocks.Cli -- file-signing-key\" or \"openssl rand -base64 32\".");
        }
        else
        {
            var bytes = new byte[options.SigningKey.Length];
            if (!Convert.TryFromBase64String(options.SigningKey.Trim(), bytes, out var written) || written < MinKeyBytes)
                failures.Add(
                    $"{BlocksFilesOptions.SigningKeyKey} (environment variable Blocks__Files__SigningKey) must be base64 of at least 32 bytes.");
        }

        if (options.UrlLifetime < BlocksFilesOptions.MinUrlLifetime || options.UrlLifetime > BlocksFilesOptions.MaxUrlLifetime)
            failures.Add(
                $"{BlocksFilesOptions.UrlLifetimeKey} (environment variable Blocks__Files__UrlLifetime) must be between 00:01:00 and 7.00:00:00.");

        if (options.RetentionDays is < 1 or > 3650)
            failures.Add(
                $"{BlocksFilesOptions.RetentionDaysKey} (environment variable Blocks__Files__RetentionDays) must be between 1 and 3650.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
