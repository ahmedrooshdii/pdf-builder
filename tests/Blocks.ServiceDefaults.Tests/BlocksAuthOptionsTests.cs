using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>FR-001: no admin key hash, no service; and the error names the setting.</summary>
public sealed class BlocksAuthOptionsTests
{
    private static readonly string ValidHash = KeyHasher.HashAdminKey(ApiKeyFormat.NewAdminKey());

    private static ValidateOptionsResult Validate(string? hash, string code = "pdf", IReadOnlyList<string>? permissions = null) =>
        new BlocksAuthOptionsValidator().Validate(null, new BlocksAuthOptions
        {
            BlockCode = code,
            Permissions = permissions ?? ["pdf:generate"],
            AdminKeyHash = hash
        });

    [Fact]
    public void A_valid_hash_passes()
    {
        Assert.True(Validate(ValidHash).Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_hash_is_refused_naming_the_setting(string? hash)
    {
        var result = Validate(hash);

        Assert.True(result.Failed);
        Assert.Contains("Blocks:Admin:KeyHash is required", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hash_of_the_wrong_length_or_case_is_refused_naming_the_setting()
    {
        Assert.Contains("Blocks:Admin:KeyHash", Validate(ValidHash[..63]).FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Blocks:Admin:KeyHash", Validate(ValidHash.ToUpperInvariant()).FailureMessage, StringComparison.Ordinal);
        Assert.True(Validate("adm_" + new string('a', 40)).Failed);
    }

    [Fact]
    public void A_block_cannot_claim_the_admin_scope_or_the_admin_prefix()
    {
        Assert.True(Validate(ValidHash, permissions: ["admin"]).Failed);
        Assert.True(Validate(ValidHash, code: "adm").Failed);
        Assert.True(Validate(ValidHash, permissions: []).Failed);
    }

    [Fact]
    public async Task The_service_does_not_start_without_the_hash()
    {
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => StudHost.StartAsync(adminKeyHash: ""));

        Assert.Contains("Blocks:Admin:KeyHash", exception.Message, StringComparison.Ordinal);
    }

    private static BlocksAuthOptions Read(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var options = new BlocksAuthOptions { BlockCode = "pdf", Permissions = ["pdf:generate"], AdminKeyHash = ValidHash };
        options.ReadConsoleSettings(configuration);
        return options;
    }

    [Fact]
    public void The_console_session_lasts_twelve_hours_by_default()
    {
        var options = Read();

        Assert.Equal(TimeSpan.FromHours(12), options.ConsoleSessionLifetime);
        Assert.Empty(options.KnownNetworks);
        Assert.True(new BlocksAuthOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("00:04:59")]
    [InlineData("1.00:00:01")]
    [InlineData("a working day")]
    public void A_session_lifetime_out_of_range_or_unreadable_is_refused_naming_the_setting(string value)
    {
        var result = new BlocksAuthOptionsValidator().Validate(null, Read((BlocksAuthOptions.ConsoleSessionLifetimeKey, value)));

        Assert.True(result.Failed);
        Assert.Contains("Blocks:Console:SessionLifetime", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("00:05:00")]
    [InlineData("08:00:00")]
    [InlineData("1.00:00:00")]
    public void A_session_lifetime_from_five_minutes_to_a_day_is_accepted(string value)
    {
        Assert.True(new BlocksAuthOptionsValidator().Validate(null, Read((BlocksAuthOptions.ConsoleSessionLifetimeKey, value))).Succeeded);
    }

    [Fact]
    public void Known_networks_are_comma_separated_cidrs()
    {
        var options = Read((BlocksAuthOptions.KnownNetworksKey, "172.16.0.0/12, 10.0.0.0/8"));

        Assert.Equal(2, options.KnownNetworks.Count);
        Assert.True(options.KnownNetworks[0].Contains(System.Net.IPAddress.Parse("172.18.0.5")));
        Assert.True(new BlocksAuthOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void A_known_network_that_is_not_a_cidr_is_refused_naming_the_setting()
    {
        var result = new BlocksAuthOptionsValidator().Validate(null, Read((BlocksAuthOptions.KnownNetworksKey, "172.16.0.0/12,not-a-cidr")));

        Assert.True(result.Failed);
        Assert.Contains("Blocks:ForwardedHeaders:KnownNetworks", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("not-a-cidr", result.FailureMessage, StringComparison.Ordinal);
    }
}
