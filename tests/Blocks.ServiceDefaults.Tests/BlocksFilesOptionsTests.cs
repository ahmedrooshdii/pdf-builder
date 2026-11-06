using Blocks.ServiceDefaults.Files;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>
/// <c>Blocks:Files</c>: a missing or malformed setting stops the service, naming the key and never
/// the configured value (contracts/configuration.md).
/// </summary>
public sealed class BlocksFilesOptionsTests
{
    private static string Key(int bytes) => Convert.ToBase64String(new byte[bytes].Select((_, i) => (byte)(i + 1)).ToArray());

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(
        string? key, TimeSpan? lifetime = null, int retentionDays = 30) =>
        new BlocksFilesOptionsValidator().Validate(null, new BlocksFilesOptions
        {
            SigningKey = key,
            UrlLifetime = lifetime ?? TimeSpan.FromHours(1),
            RetentionDays = retentionDays
        });

    [Fact]
    public void A_32_byte_key_passes() => Assert.True(Validate(Key(32)).Succeeded);

    [Fact]
    public void The_defaults_are_the_documented_ones()
    {
        var options = new BlocksFilesOptions();

        Assert.Equal(TimeSpan.FromHours(1), options.UrlLifetime);
        Assert.Equal(30, options.RetentionDays);
        Assert.False(options.RequireSignature);
    }

    [Fact]
    public void A_missing_key_is_refused_naming_the_setting_and_how_to_make_one()
    {
        var result = Validate(null);

        Assert.True(result.Failed);
        Assert.Contains("Blocks:Files:SigningKey", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Blocks__Files__SigningKey", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("file-signing-key", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("openssl rand -base64 32", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("this is not base64!!")]
    [InlineData("c2hvcnQ=")]
    public void A_malformed_or_short_key_is_refused_without_echoing_it(string key)
    {
        var result = Validate(key);

        Assert.True(result.Failed);
        Assert.Contains("Blocks:Files:SigningKey", result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(key, result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_31_byte_key_is_refused_and_not_echoed()
    {
        var key = Key(31);

        var result = Validate(key);

        Assert.True(result.Failed);
        Assert.DoesNotContain(key, result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("00:00:59", false)]
    [InlineData("7.00:00:01", false)]
    [InlineData("00:01:00", true)]
    [InlineData("7.00:00:00", true)]
    public void The_url_lifetime_has_a_floor_and_a_ceiling(string lifetime, bool valid)
    {
        var result = Validate(Key(32), TimeSpan.Parse(lifetime, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(valid, result.Succeeded);
        if (!valid)
            Assert.Contains("Blocks:Files:UrlLifetime", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3651, false)]
    [InlineData(1, true)]
    [InlineData(3650, true)]
    public void Retention_is_between_one_and_3650_days(int days, bool valid)
    {
        var result = Validate(Key(32), retentionDays: days);

        Assert.Equal(valid, result.Succeeded);
        if (!valid)
            Assert.Contains("Blocks:Files:RetentionDays", result.FailureMessage, StringComparison.Ordinal);
    }
}
