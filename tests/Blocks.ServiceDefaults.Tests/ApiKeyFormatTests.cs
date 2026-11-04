using System.Text.RegularExpressions;
using Blocks.ServiceDefaults.Authentication;

namespace Blocks.ServiceDefaults.Tests;

public sealed class ApiKeyFormatTests
{
    private const string Block = "pdf";

    [Fact]
    public void A_client_key_carries_the_block_code_a_public_id_and_a_40_character_secret()
    {
        var key = ApiKeyFormat.ClientKey(Block, ApiKeyFormat.NewKeyId(), ApiKeyFormat.NewSecret());

        Assert.Matches(new Regex("^pdf_[a-z0-9]{8}_[A-Za-z0-9]{40}$"), key);
    }

    [Fact]
    public void An_admin_key_is_adm_and_a_40_character_secret()
    {
        Assert.Matches(new Regex("^adm_[A-Za-z0-9]{40}$"), ApiKeyFormat.NewAdminKey());
    }

    [Fact]
    public void Generated_values_differ_every_time()
    {
        var secrets = Enumerable.Range(0, 1000).Select(_ => ApiKeyFormat.NewSecret()).ToHashSet();

        Assert.Equal(1000, secrets.Count);
    }

    [Fact]
    public void A_client_key_parses_back_into_its_id_and_secret()
    {
        var keyId = ApiKeyFormat.NewKeyId();
        var secret = ApiKeyFormat.NewSecret();

        var parsed = ApiKeyFormat.Parse(ApiKeyFormat.ClientKey(Block, keyId, secret), Block);

        Assert.Equal(new ParsedApiKey(ApiKeyKind.Client, keyId, secret), parsed);
    }

    [Fact]
    public void An_admin_key_parses_as_admin()
    {
        Assert.Equal(ApiKeyKind.Admin, ApiKeyFormat.Parse(ApiKeyFormat.NewAdminKey(), Block).Kind);
    }

    public static TheoryData<string> MalformedKeys()
    {
        var valid = ApiKeyFormat.ClientKey(Block, "k7f2a9c1", new string('a', 40));

        return
        [
            "",
            " ",
            "banana",
            " " + valid,
            valid + " ",
            valid + "\n",
            valid + "a",
            valid[..^1],
            "eml" + valid[3..],                                  // another block's key
            "PDF" + valid[3..],
            valid.Replace("k7f2a9c1", "K7F2A9C1", StringComparison.Ordinal),
            valid.Replace("k7f2a9c1", "k7f2-9c1", StringComparison.Ordinal),
            valid.Replace("_k7f2a9c1_", "-k7f2a9c1_", StringComparison.Ordinal),
            valid[..^1] + "!",
            valid + ", " + valid,                                // two keys folded into one header
            new string('a', 10_000),
            "adm_" + new string('a', 39),
            "adm_" + new string('a', 41),
            "adm_" + new string('a', 39) + "-",
            "pdf_zzzzzzzz_notarealsecret0000000000000000000000"   // the quickstart's made-up key
        ];
    }

    [Theory]
    [MemberData(nameof(MalformedKeys))]
    public void Anything_else_is_malformed(string value)
    {
        Assert.Equal(ApiKeyKind.Malformed, ApiKeyFormat.Parse(value, Block).Kind);
    }

    [Fact]
    public void No_value_is_malformed()
    {
        Assert.Equal(ApiKeyKind.Malformed, ApiKeyFormat.Parse(null, Block).Kind);
    }

    [Fact]
    public void Lists_show_the_key_id_and_nothing_usable()
    {
        Assert.Equal("pdf_k7f2a9c1_…", ApiKeyFormat.Display(Block, "k7f2a9c1"));
    }

    [Theory]
    [InlineData("pdf", true)]
    [InlineData("eml", true)]
    [InlineData("adm", false)]
    [InlineData("p", false)]
    [InlineData("Pdf", false)]
    [InlineData("pdf1", false)]
    [InlineData("", false)]
    public void Block_codes_are_short_lowercase_words(string code, bool valid)
    {
        Assert.Equal(valid, ApiKeyFormat.IsValidBlockCode(code));
    }
}
