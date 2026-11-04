using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Tests.Support;
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
}
