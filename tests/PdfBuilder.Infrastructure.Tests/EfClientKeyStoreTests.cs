using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Clients;
using Blocks.ServiceDefaults.EntityFramework;
using Blocks.ServiceDefaults.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Infrastructure.Tests;

/// <summary>The shared clients-and-keys store over PDF Builder's own database.</summary>
public sealed class EfClientKeyStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly string[] Generate = ["pdf:generate"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICurrentClient>(_ => new FixedCurrentClient());   // the context needs a caller; these tests are about no client in particular
        services.AddDbContext<PdfBuilderDbContext>(options => options.Configure(connectionString));
        services.AddBlocksClientStore<PdfBuilderDbContext>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>Each call in its own scope, like separate requests.</summary>
    private static async Task<T> WithStoreAsync<T>(ServiceProvider provider, Func<IClientKeyStore, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IClientKeyStore>());
    }

    private static ApiKeyInfo NewKey(string clientId, DateTimeOffset createdAt, string? keyId = null) => new(
        keyId ?? ApiKeyFormat.NewKeyId(), clientId, "production", Generate,
        KeyHasher.HashSecret(ApiKeyFormat.NewSecret()), createdAt, ExpiresAt: null, RevokedAt: null, LastUsedAt: null);

    [Fact]
    public async Task A_registered_client_is_read_back_and_listed_by_id()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(database.ConnectionString);

        Assert.True(await WithStoreAsync(provider, s => s.AddClientAsync(new ClientInfo("erp", "ERP", Now, null), Ct)));
        Assert.True(await WithStoreAsync(provider, s => s.AddClientAsync(new ClientInfo("ecommerce", "E-commerce", Now, null), Ct)));

        var client = await WithStoreAsync(provider, s => s.GetClientAsync("ecommerce", Ct));
        var all = await WithStoreAsync(provider, s => s.ListClientsAsync(Ct));

        Assert.Equal(new ClientInfo("ecommerce", "E-commerce", Now, null), client);
        Assert.Equal(["ecommerce", "erp"], all.Select(c => c.Id));
        Assert.Null(await WithStoreAsync(provider, s => s.GetClientAsync("missing", Ct)));
    }

    [Fact]
    public async Task A_client_id_is_taken_for_good_even_while_disabled()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(database.ConnectionString);

        await WithStoreAsync(provider, s => s.AddClientAsync(new ClientInfo("ecommerce", "E-commerce", Now, Now), Ct));

        Assert.False(await WithStoreAsync(provider, s => s.AddClientAsync(new ClientInfo("ecommerce", "Again", Now, null), Ct)));
    }

    [Fact]
    public async Task A_key_is_found_by_its_id_with_its_client_state_and_stored_only_as_a_hash()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(database.ConnectionString);
        await WithStoreAsync(provider, s => s.AddClientAsync(new ClientInfo("ecommerce", "E-commerce", Now, null), Ct));

        var secret = ApiKeyFormat.NewSecret();
        var key = NewKey("ecommerce", Now) with { SecretHash = KeyHasher.HashSecret(secret), ExpiresAt = Now.AddDays(30) };
        Assert.True(await WithStoreAsync(provider, s => s.AddKeyAsync(key, Ct)));

        var found = await WithStoreAsync(provider, s => s.FindKeyAsync(key.KeyId, Ct));

        Assert.NotNull(found);
        Assert.False(found.ClientDisabled);
        Assert.Equal(key.KeyId, found.Key.KeyId);
        Assert.Equal(key.SecretHash.ToArray(), found.Key.SecretHash.ToArray());
        Assert.Equal(key.ExpiresAt, found.Key.ExpiresAt);
        Assert.Equal(Generate, found.Key.Scopes);
        Assert.Null(await WithStoreAsync(provider, s => s.FindKeyAsync("zzzzzzzz", Ct)));

        // Nothing stored is the secret: the hash column is 32 bytes and no text column holds it.
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT octet_length(secret_hash), row_to_json(api_keys)::text FROM api_keys", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct));
        Assert.Equal(32, reader.GetInt32(0));
        Assert.DoesNotContain(secret, reader.GetString(1), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_used_key_id_is_reported_so_the_caller_can_draw_another()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(database.ConnectionString);
        await WithStoreAsync(provider, s => s.AddClientAsync(new ClientInfo("ecommerce", "E-commerce", Now, null), Ct));

        Assert.True(await WithStoreAsync(provider, s => s.AddKeyAsync(NewKey("ecommerce", Now, "k7f2a9c1"), Ct)));
        Assert.False(await WithStoreAsync(provider, s => s.AddKeyAsync(NewKey("ecommerce", Now, "k7f2a9c1"), Ct)));
    }

    [Fact]
    public async Task Keys_are_listed_newest_first()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(database.ConnectionString);
        await WithStoreAsync(provider, s => s.AddClientAsync(new ClientInfo("ecommerce", "E-commerce", Now, null), Ct));

        var older = NewKey("ecommerce", Now);
        var newer = NewKey("ecommerce", Now.AddMinutes(1));
        await WithStoreAsync(provider, s => s.AddKeyAsync(older, Ct));
        await WithStoreAsync(provider, s => s.AddKeyAsync(newer, Ct));

        var keys = await WithStoreAsync(provider, s => s.ListKeysAsync("ecommerce", Ct));

        Assert.Equal([newer.KeyId, older.KeyId], keys.Select(k => k.KeyId));
    }

    [Fact]
    public async Task Revocation_is_recorded_once_and_only_for_the_owning_client()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(database.ConnectionString);
        await WithStoreAsync(provider, s => s.AddClientAsync(new ClientInfo("ecommerce", "E-commerce", Now, null), Ct));
        var key = NewKey("ecommerce", Now);
        await WithStoreAsync(provider, s => s.AddKeyAsync(key, Ct));

        Assert.Null(await WithStoreAsync(provider, s => s.RevokeKeyAsync("erp", key.KeyId, Now, Ct)));

        var first = await WithStoreAsync(provider, s => s.RevokeKeyAsync("ecommerce", key.KeyId, Now.AddMinutes(1), Ct));
        var second = await WithStoreAsync(provider, s => s.RevokeKeyAsync("ecommerce", key.KeyId, Now.AddMinutes(2), Ct));

        Assert.Equal(Now.AddMinutes(1), first?.RevokedAt);
        Assert.Equal(Now.AddMinutes(1), second?.RevokedAt);
        Assert.Equal(ApiKeyStatus.Revoked, second?.StatusAt(Now.AddMinutes(2)));
    }

    [Fact]
    public async Task A_client_is_disabled_enabled_and_renamed_and_its_keys_see_the_state()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(database.ConnectionString);
        await WithStoreAsync(provider, s => s.AddClientAsync(new ClientInfo("ecommerce", "E-commerce", Now, null), Ct));
        var key = NewKey("ecommerce", Now);
        await WithStoreAsync(provider, s => s.AddKeyAsync(key, Ct));

        var disabled = await WithStoreAsync(provider, s => s.UpdateClientAsync("ecommerce", null, true, Now.AddMinutes(1), Ct));
        var disabledAgain = await WithStoreAsync(provider, s => s.UpdateClientAsync("ecommerce", "Shop", true, Now.AddMinutes(2), Ct));
        var lookup = await WithStoreAsync(provider, s => s.FindKeyAsync(key.KeyId, Ct));
        var enabled = await WithStoreAsync(provider, s => s.UpdateClientAsync("ecommerce", null, false, Now.AddMinutes(3), Ct));

        Assert.Equal(Now.AddMinutes(1), disabled?.DisabledAt);
        Assert.Equal(Now.AddMinutes(1), disabledAgain?.DisabledAt);
        Assert.Equal("Shop", disabledAgain?.Name);
        Assert.True(lookup?.ClientDisabled);
        Assert.Null(enabled?.DisabledAt);
        Assert.Null(await WithStoreAsync(provider, s => s.UpdateClientAsync("missing", null, true, Now, Ct)));
    }

    [Fact]
    public async Task Last_use_is_written_for_every_key_in_a_batch_and_never_moves_backwards()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(database.ConnectionString);
        await WithStoreAsync(provider, s => s.AddClientAsync(new ClientInfo("ecommerce", "E-commerce", Now, null), Ct));
        var a = NewKey("ecommerce", Now);
        var b = NewKey("ecommerce", Now);
        var unused = NewKey("ecommerce", Now);
        foreach (var key in new[] { a, b, unused })
            await WithStoreAsync(provider, s => s.AddKeyAsync(key, Ct));

        await WithStoreAsync(provider, async s => { await s.RecordLastUsedAsync([a.KeyId, b.KeyId], Now.AddMinutes(5), Ct); return 0; });
        await WithStoreAsync(provider, async s => { await s.RecordLastUsedAsync([a.KeyId], Now.AddMinutes(4), Ct); return 0; });

        var keys = (await WithStoreAsync(provider, s => s.ListKeysAsync("ecommerce", Ct))).ToDictionary(k => k.KeyId);
        Assert.Equal(Now.AddMinutes(5), keys[a.KeyId].LastUsedAt);
        Assert.Equal(Now.AddMinutes(5), keys[b.KeyId].LastUsedAt);
        Assert.Null(keys[unused.KeyId].LastUsedAt);
    }

    [Theory]
    [InlineData("INSERT INTO clients (id, name, created_at) VALUES ('E-Commerce', 'x', now())", "ck_clients_id_format")]
    [InlineData("INSERT INTO api_keys (key_id, client_id, name, scopes, secret_hash, created_at) VALUES ('k7f2a9c1', 'ecommerce', 'x', '{}', decode(repeat('00', 32), 'hex'), now())", "ck_api_keys_scopes_not_empty")]
    [InlineData("INSERT INTO api_keys (key_id, client_id, name, scopes, secret_hash, created_at) VALUES ('k7f2a9c1', 'ecommerce', 'x', '{pdf:generate}', decode(repeat('00', 31), 'hex'), now())", "ck_api_keys_secret_hash_length")]
    [InlineData("INSERT INTO api_keys (key_id, client_id, name, scopes, secret_hash, created_at) VALUES ('K7F2A9C1', 'ecommerce', 'x', '{pdf:generate}', decode(repeat('00', 32), 'hex'), now())", "ck_api_keys_key_id_format")]
    public async Task The_database_enforces_the_rules_for_rows_written_outside_the_service(string sql, string constraint)
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using (var client = new NpgsqlCommand("INSERT INTO clients (id, name, created_at) VALUES ('ecommerce', 'E', now())", connection))
            await client.ExecuteNonQueryAsync(Ct);

        await using var command = new NpgsqlCommand(sql, connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));

        Assert.Equal(constraint, error.ConstraintName);
    }

    /// <summary>
    /// FR-021: an unreachable database surfaces as <see cref="StorageUnavailableException"/>, never as
    /// "no such key". Needs no server: port 1 refuses the connection at once.
    /// </summary>
    [Fact]
    public async Task An_unreachable_database_is_reported_as_storage_unavailable()
    {
        await using var provider = BuildProvider("Host=127.0.0.1;Port=1;Database=nowhere;Timeout=2;Pooling=false");

        var error = await Assert.ThrowsAsync<StorageUnavailableException>(
            () => WithStoreAsync(provider, s => s.FindKeyAsync("k7f2a9c1", Ct)));

        Assert.True(StorageFailures.IsUnavailable(error));
    }
}
