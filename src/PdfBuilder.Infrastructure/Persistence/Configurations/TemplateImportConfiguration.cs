using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PdfBuilder.Infrastructure.Persistence.Records;

namespace PdfBuilder.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>template_imports</c>: one row, written once, when the file import completes.</summary>
internal static class TemplateImportConfiguration
{
    public static void Configure(EntityTypeBuilder<TemplateImportRecord> import)
    {
        import.ToTable("template_imports", table => table.HasCheckConstraint(
            "ck_template_imports_single_row", $"id = {TemplateImportRecord.SingleRowId}"));

        import.HasKey(i => i.Id);
        import.Property(i => i.Id).ValueGeneratedNever();
        import.Property(i => i.SourcePath).IsRequired();
        import.Property(i => i.SkippedJson).HasColumnName("skipped").HasColumnType("jsonb").IsRequired();
        import.Ignore(i => i.SkippedFiles);
    }
}
