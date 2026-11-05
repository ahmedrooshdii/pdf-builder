using Microsoft.Extensions.Options;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>
/// <c>Blocks:Bus:DefaultClientId</c>: optional, but a malformed value is a typo, so the service
/// refuses to start and the error names the setting (contracts/configuration.md).
/// </summary>
public sealed class BlocksBusOptionsTests
{
    private static ValidateOptionsResult Validate(string? defaultClientId) =>
        new BlocksBusOptionsValidator().Validate(null, new BlocksBusOptions { DefaultClientId = defaultClientId });

    [Theory]
    [InlineData(null)]
    [InlineData("ecommerce")]
    [InlineData("erp-2")]
    [InlineData("a")]
    public void Unset_or_a_valid_client_id_passes(string? value)
    {
        Assert.True(Validate(value).Succeeded);
    }

    [Theory]
    [InlineData("Ecommerce")]
    [InlineData("1abc")]
    [InlineData("has space")]
    [InlineData("under_score")]
    [InlineData("")]
    public void A_malformed_client_id_is_refused_naming_the_setting(string value)
    {
        var result = Validate(value);

        Assert.True(result.Failed);
        Assert.Contains("Blocks:Bus:DefaultClientId", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Blocks__Bus__DefaultClientId", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_client_id_of_forty_one_characters_is_refused()
    {
        Assert.True(Validate("a" + new string('b', 40)).Failed);
        Assert.True(Validate("a" + new string('b', 39)).Succeeded);
    }
}
