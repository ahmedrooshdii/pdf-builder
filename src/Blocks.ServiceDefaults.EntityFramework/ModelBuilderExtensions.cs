using Microsoft.EntityFrameworkCore;

namespace Blocks.ServiceDefaults.EntityFramework;

/// <summary>Adds the <c>clients</c> and <c>api_keys</c> tables to a block's own <see cref="DbContext"/>.</summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Every table, column, key, index and constraint name is spelled out, so the schema is the same
    /// whatever naming convention the block's context uses.
    /// </summary>
    public static ModelBuilder AddBlocksClients(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ClientRecord>(client =>
        {
            // The rule is in the database too, so a row written outside the service cannot break it.
            client.ToTable("clients", table =>
                table.HasCheckConstraint("ck_clients_id_format", "id ~ '^[a-z][a-z0-9-]{0,39}$'"));

            client.HasKey(c => c.Id).HasName("pk_clients");

            client.Property(c => c.Id).HasColumnName("id").HasMaxLength(40);
            client.Property(c => c.Name).HasColumnName("name").HasMaxLength(100);
            client.Property(c => c.CreatedAt).HasColumnName("created_at");
            client.Property(c => c.DisabledAt).HasColumnName("disabled_at");
        });

        modelBuilder.Entity<ApiKeyRecord>(key =>
        {
            key.ToTable("api_keys", table =>
            {
                table.HasCheckConstraint("ck_api_keys_scopes_not_empty", "cardinality(scopes) > 0");
                table.HasCheckConstraint("ck_api_keys_secret_hash_length", "octet_length(secret_hash) = 32");
                table.HasCheckConstraint("ck_api_keys_key_id_format", "key_id ~ '^[a-z0-9]{8}$'");
            });

            key.HasKey(k => k.KeyId).HasName("pk_api_keys");

            key.Property(k => k.KeyId).HasColumnName("key_id").HasMaxLength(8).IsFixedLength();
            key.Property(k => k.ClientId).HasColumnName("client_id").HasMaxLength(40);
            key.Property(k => k.Name).HasColumnName("name").HasMaxLength(100);
            key.Property(k => k.Scopes).HasColumnName("scopes").HasColumnType("text[]");
            key.Property(k => k.SecretHash).HasColumnName("secret_hash");
            key.Property(k => k.CreatedAt).HasColumnName("created_at");
            key.Property(k => k.ExpiresAt).HasColumnName("expires_at");
            key.Property(k => k.RevokedAt).HasColumnName("revoked_at");
            key.Property(k => k.LastUsedAt).HasColumnName("last_used_at");

            // Clients are never deleted; RESTRICT makes sure a mistake cannot take their keys with them.
            key.HasOne<ClientRecord>()
                .WithMany()
                .HasForeignKey(k => k.ClientId)
                .HasConstraintName("fk_api_keys_clients_client_id")
                .OnDelete(DeleteBehavior.Restrict);

            key.HasIndex(k => k.ClientId).HasDatabaseName("ix_api_keys_client_id");
        });

        return modelBuilder;
    }
}
