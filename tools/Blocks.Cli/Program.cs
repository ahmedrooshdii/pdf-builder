using Blocks.ServiceDefaults;
using System.Security.Cryptography;
using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Files;

// Operator tooling for every block. Client and key management goes through the block's
// /api/admin endpoints (see samples/admin.http); this tool only does what no endpoint can:
// creating the admin key that those endpoints require.

return args switch
{
    ["admin-key"] => AdminKey(),
    ["file-signing-key"] => FileSigningKey(),
    _ => Usage()
};

static int AdminKey()
{
    var key = ApiKeyFormat.NewAdminKey();

    Console.WriteLine("Admin key (shown once, store it somewhere safe):");
    Console.WriteLine($"  {key}");
    Console.WriteLine($"Put this in configuration as {BlocksAuthOptions.AdminKeyHashKey}:");
    Console.WriteLine($"  {KeyHasher.HashAdminKey(key)}");
    return 0;
}

static int FileSigningKey()
{
    // 32 random bytes, which is what Blocks:Files:SigningKey asks for. Nothing is derived from the admin key.
    Console.WriteLine(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    Console.WriteLine($"Put this in configuration as {BlocksFilesOptions.SigningKeyKey} (environment variable Blocks__Files__SigningKey).");
    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/Blocks.Cli -- <command>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Commands:");
    Console.Error.WriteLine("  admin-key         Generate a new admin key and the hash to configure for it.");
    Console.Error.WriteLine("  file-signing-key  Generate a key for signing file links (Blocks:Files:SigningKey).");
    return 2;
}
