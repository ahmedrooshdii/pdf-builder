using Blocks.ServiceDefaults.Identity;
using Microsoft.Extensions.Options;

namespace Blocks.ServiceDefaults;

/// <summary>What a block needs to know about messages that arrive on its bus, from configuration.</summary>
public sealed class BlocksBusOptions
{
    /// <summary>The configuration section of these options.</summary>
    public const string Section = "Blocks:Bus";

    /// <summary>The configuration key of <see cref="DefaultClientId"/>, quoted in start-up errors.</summary>
    public const string DefaultClientIdKey = "Blocks:Bus:DefaultClientId";

    /// <summary>
    /// The client assumed for a message that names none, and the owner given to data that existed
    /// before clients did. Unset by default; when set it must be a valid client id.
    /// </summary>
    public string? DefaultClientId { get; set; }
}

/// <summary>Refuses to start with a malformed default client id, naming the setting. Unset is valid.</summary>
internal sealed class BlocksBusOptionsValidator : IValidateOptions<BlocksBusOptions>
{
    public ValidateOptionsResult Validate(string? name, BlocksBusOptions options)
    {
        if (options.DefaultClientId is null || HttpCurrentClient.IsClientId(options.DefaultClientId))
            return ValidateOptionsResult.Success;

        return ValidateOptionsResult.Fail(
            $"{BlocksBusOptions.DefaultClientIdKey} (environment variable Blocks__Bus__DefaultClientId) must be a client id: " +
            "a lowercase letter followed by up to 39 lowercase letters, digits or hyphens.");
    }
}
