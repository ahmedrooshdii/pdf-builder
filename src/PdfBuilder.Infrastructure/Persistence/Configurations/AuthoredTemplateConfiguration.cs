using System.Text.Json;
using Blocks.ServiceDefaults.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PdfBuilder.Domain.Templates;
using PdfBuilder.Infrastructure.Persistence.Records;

namespace PdfBuilder.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>authored_templates</c>, as laid out in specs/001-persistent-storage/data-model.md.</summary>
internal static class AuthoredTemplateConfiguration
{
    private const string KeyLowerProperty = "KeyLower";

    /// <summary>The width of <c>clients.id</c>.</summary>
    private const int ClientIdMaxLength = 40;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Configure(EntityTypeBuilder<AuthoredTemplateRecord> template)
    {
        template.ToTable("authored_templates");

        // A template belongs to one client, and the same key may exist once in each (FR-006), so the owner
        // is part of the identity. The client filter and the uniqueness rule both depend on it.
        template.HasKey(t => new { t.ClientId, t.Key });
        template.Property(t => t.ClientId).HasMaxLength(ClientIdMaxLength).IsRequired();
        template.Property(t => t.Key).HasMaxLength(TemplateKey.MaxLength).ValueGeneratedNever();
        template.HasClientOwner(t => t.ClientId, "fk_authored_templates_clients_client_id");

        // Content is always read and written whole and nothing queries inside it, so a converter to
        // JSON text is enough; the column type makes it jsonb on the server.
        template.Property(t => t.Draft)
            .HasColumnType("jsonb")
            .HasConversion(content => Serialize(content), json => Deserialize(json))
            .IsRequired();

        template.Property(t => t.Published)
            .HasColumnType("jsonb")
            .HasConversion(content => Serialize(content), json => Deserialize(json));

        // Keys are unique within a client, ignoring case ("Invoice" and "invoice" are the same template to
        // TemplateCatalog), while lookups stay exact-case as they are on a Linux file system today.
        // The server derives this column, so the rule is part of the model rather than hand-written SQL
        // in a migration.
        template.Property<string>(KeyLowerProperty)
            .HasColumnName("key_lower")
            .HasComputedColumnSql("lower(\"key\")", stored: true);
        template.HasIndex(nameof(AuthoredTemplateRecord.ClientId), KeyLowerProperty)
            .IsUnique()
            .HasDatabaseName("ux_authored_templates_client_key_lower");
    }

    private static string Serialize(TemplateContentRecord? content) => JsonSerializer.Serialize(content, Json);

    private static TemplateContentRecord Deserialize(string json) =>
        JsonSerializer.Deserialize<TemplateContentRecord>(json, Json)!;
}
