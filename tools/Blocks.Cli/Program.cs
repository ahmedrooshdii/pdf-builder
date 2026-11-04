using Blocks.ServiceDefaults;
using Blocks.ServiceDefaults.Authentication;

// Operator tooling for every block. Client and key management goes through the block's
// /api/admin endpoints (see samples/admin.http); this tool only does what no endpoint can:
// creating the admin key that those endpoints require.

return args switch
{
    ["admin-key"] => AdminKey(),
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

static int Usage()
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/Blocks.Cli -- <command>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Commands:");
    Console.Error.WriteLine("  admin-key   Generate a new admin key and the hash to configure for it.");
    return 2;
}
