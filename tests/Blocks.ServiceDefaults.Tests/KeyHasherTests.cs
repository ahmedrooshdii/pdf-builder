using System.Security.Cryptography;
using System.Text;
using Blocks.ServiceDefaults.Authentication;

namespace Blocks.ServiceDefaults.Tests;

public sealed class KeyHasherTests
{
    [Fact]
    public void A_secret_is_stored_as_its_32_byte_SHA256()
    {
        var secret = ApiKeyFormat.NewSecret();

        var hash = KeyHasher.HashSecret(secret);

        Assert.Equal(32, hash.Length);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(secret)), hash);
        Assert.DoesNotContain(secret, Encoding.UTF8.GetString(hash), StringComparison.Ordinal);
    }

    [Fact]
    public void The_admin_key_hash_is_64_lowercase_hex_characters_of_the_whole_key()
    {
        var key = ApiKeyFormat.NewAdminKey();

        var hash = KeyHasher.HashAdminKey(key);

        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key))), hash);
    }

    [Fact]
    public void Matches_compares_whole_values()
    {
        var hash = KeyHasher.HashSecret("a");

        Assert.True(KeyHasher.Matches(hash, KeyHasher.HashSecret("a")));
        Assert.False(KeyHasher.Matches(hash, KeyHasher.HashSecret("b")));
        Assert.False(KeyHasher.Matches(hash, hash.AsSpan(0, 31)));
    }
}
