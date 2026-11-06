using System.Globalization;
using Blocks.ServiceDefaults.Files;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>
/// The signature on a file link (research R1): valid for the file it was made for until it expires, and
/// nothing else is. Anything that does not check out is just <see cref="FileLinkCheck.Invalid"/>.
/// </summary>
public sealed class FileLinkSignerTests
{
    private const string FileId = "a7e0495b2cd24ed9bb65b1b0f3a14c20";
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
    private static readonly string OtherKey = Convert.ToBase64String(Enumerable.Range(2, 32).Select(i => (byte)i).ToArray());
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static FileLinkSigner SignerWith(string key, TimeSpan? lifetime = null) =>
        new(Options.Create(new BlocksFilesOptions { SigningKey = key, UrlLifetime = lifetime ?? TimeSpan.FromHours(1) }));

    private static QueryCollection Parse(string query) =>
        new(QueryHelpers.ParseQuery("?" + query));

    private static string Part(string query, string name) =>
        query.Split('&').Single(p => p.StartsWith(name + "=", StringComparison.Ordinal))[(name.Length + 1)..];

    [Fact]
    public void A_signed_link_is_valid_for_its_file_until_it_expires()
    {
        var signer = SignerWith(Key);

        var signed = signer.Sign(FileId, Now);

        Assert.Equal(FileLinkCheck.Valid, signer.Check(FileId, Parse(signed.Query), Now));
        Assert.Equal(FileLinkCheck.Valid, signer.Check(FileId, Parse(signed.Query), signed.ExpiresAt.AddSeconds(-1)));
    }

    [Fact]
    public void The_expiry_is_the_lifetime_after_now_in_unix_seconds()
    {
        var signed = SignerWith(Key, TimeSpan.FromMinutes(2)).Sign(FileId, Now);

        Assert.Equal(Now.AddMinutes(2), signed.ExpiresAt);
        Assert.Equal(
            Now.AddMinutes(2).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            Part(signed.Query, "expires"));
    }

    [Fact]
    public void A_fraction_of_a_second_rounds_the_expiry_up_so_a_link_never_lives_shorter_than_asked()
    {
        var signed = SignerWith(Key, TimeSpan.FromMinutes(2)).Sign(FileId, Now.AddMilliseconds(400));

        Assert.Equal(Now.AddMinutes(2).AddSeconds(1), signed.ExpiresAt);
    }

    [Fact]
    public void A_link_is_invalid_from_the_second_it_expires()
    {
        var signer = SignerWith(Key);
        var signed = signer.Sign(FileId, Now);

        Assert.Equal(FileLinkCheck.Invalid, signer.Check(FileId, Parse(signed.Query), signed.ExpiresAt));
        Assert.Equal(FileLinkCheck.Invalid, signer.Check(FileId, Parse(signed.Query), signed.ExpiresAt.AddDays(1)));
    }

    [Fact]
    public void A_link_made_for_one_file_does_not_open_another()
    {
        var signer = SignerWith(Key);
        var signed = signer.Sign(FileId, Now);

        Assert.Equal(FileLinkCheck.Invalid, signer.Check("b" + FileId[1..], Parse(signed.Query), Now));
    }

    [Fact]
    public void A_later_expiry_with_the_old_signature_is_invalid()
    {
        var signer = SignerWith(Key);
        var signed = signer.Sign(FileId, Now);
        var expires = long.Parse(Part(signed.Query, "expires"), CultureInfo.InvariantCulture);

        var query = $"expires={expires + 3600}&sig={Part(signed.Query, "sig")}";

        Assert.Equal(FileLinkCheck.Invalid, signer.Check(FileId, Parse(query), Now));
    }

    [Fact]
    public void Changing_any_single_character_of_the_signature_makes_it_invalid()
    {
        var signer = SignerWith(Key);
        var signed = signer.Sign(FileId, Now);
        var expires = Part(signed.Query, "expires");
        var sig = Part(signed.Query, "sig");

        for (var i = 0; i < sig.Length; i++)
        {
            var altered = sig[..i] + (sig[i] == 'A' ? 'B' : 'A') + sig[(i + 1)..];
            Assert.Equal(FileLinkCheck.Invalid, signer.Check(FileId, Parse($"expires={expires}&sig={altered}"), Now));
        }
    }

    [Fact]
    public void A_signature_from_another_key_is_invalid()
    {
        var signed = SignerWith(OtherKey).Sign(FileId, Now);

        Assert.Equal(FileLinkCheck.Invalid, SignerWith(Key).Check(FileId, Parse(signed.Query), Now));
    }

    [Fact]
    public void Two_signers_with_the_same_key_accept_links_made_by_each_other()
    {
        var signed = SignerWith(Key).Sign(FileId, Now);

        Assert.Equal(FileLinkCheck.Valid, SignerWith(Key).Check(FileId, Parse(signed.Query), Now));
    }

    [Theory]
    [InlineData("padding")]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("alphabet")]
    public void A_signature_of_the_wrong_shape_is_invalid(string kind)
    {
        var signer = SignerWith(Key);
        var signed = signer.Sign(FileId, Now);
        var expires = Part(signed.Query, "expires");
        var sig = Part(signed.Query, "sig");

        var bad = kind switch
        {
            "padding" => sig + "=",
            "short" => sig[..42],
            "long" => sig + "A",
            _ => sig[..^1] + "+"
        };

        Assert.Equal(FileLinkCheck.Invalid, signer.Check(FileId, Parse($"expires={expires}&sig={bad}"), Now));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1e10")]
    [InlineData("1234567890123")]      // thirteen digits
    [InlineData("")]
    public void An_expiry_that_is_not_one_to_twelve_digits_is_invalid(string expires)
    {
        var signer = SignerWith(Key);
        var sig = Part(signer.Sign(FileId, Now).Query, "sig");

        Assert.Equal(FileLinkCheck.Invalid, signer.Check(FileId, Parse($"expires={expires}&sig={sig}"), Now));
    }

    [Fact]
    public void A_parameter_given_twice_is_invalid()
    {
        var signer = SignerWith(Key);
        var signed = signer.Sign(FileId, Now);

        Assert.Equal(FileLinkCheck.Invalid, signer.Check(FileId, Parse(signed.Query + "&sig=" + Part(signed.Query, "sig")), Now));
        Assert.Equal(FileLinkCheck.Invalid, signer.Check(FileId, Parse(signed.Query + "&expires=1"), Now));
    }

    [Fact]
    public void Only_one_of_the_two_parameters_is_invalid_and_neither_is_not_a_signature()
    {
        var signer = SignerWith(Key);
        var signed = signer.Sign(FileId, Now);

        Assert.Equal(FileLinkCheck.Invalid, signer.Check(FileId, Parse("expires=" + Part(signed.Query, "expires")), Now));
        Assert.Equal(FileLinkCheck.Invalid, signer.Check(FileId, Parse("sig=" + Part(signed.Query, "sig")), Now));
        Assert.Equal(FileLinkCheck.NoSignature, signer.Check(FileId, QueryCollection.Empty, Now));
        Assert.Equal(FileLinkCheck.NoSignature, signer.Check(FileId, Parse("download=1"), Now));
    }
}
