using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PdfBuilder.Domain.Templates;
using PdfBuilder.Infrastructure.Persistence.Records;

namespace PdfBuilder.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>authored_templates</c>, as laid out in specs/001-persistent-storage/data-model.md.</summary>
internal static class AuthoredTemplateConfiguration
{
    private const string KeyLowerProperty = "KeyLower";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Configure(EntityTypeBuilder<AuthoredTemplateRecord> template)
    {
        template.ToTable("authored_templates");

        template.HasKey(t => t.Key);
        template.Property(t => t.Key).HasMaxLength(TemplateKey.MaxLength).ValueGeneratedNever();

        // Content is always read and written whole and nothing queries inside it, so a converter to
        // JSON text is enough; the column type makes it jsonb on the server.
        template.Property(t => t.Draft)
            .HasColumnType("jsonb")
            .HasConversion(content => Serialize(content), json => Deserialize(json))
            .IsRequired();

        template.Property(t => t.Published)
            .HasColumnType("jsonb")
            .HasConversion(content => Serialize(content), json => Deserialize(json));

        // Keys are unique ignoring case ("Invoice" and "invoice" are the same template to
        // TemplateCatalog), while lookups stay exact-case as they are on a Linux file system today.
        // The server derives this column, so the rule is part of the model rather than hand-written SQL
        // in a migration.
        template.Property<string>(KeyLowerProperty)
            .HasColumnName("key_lower")
            .HasComputedColumnSql("lower(\"key\")", stored: true);
        template.HasIndex(KeyLowerProperty).IsUnique().HasDatabaseName("ux_authored_templates_key_lower");
    }

    private static string Serialize(TemplateContentRecord? content) => JsonSerializer.Serialize(content, Json);

    private static TemplateContentRecord Deserialize(string json) =>
        JsonSerializer.Deserialize<TemplateContentRecord>(json, Json)!;
}
