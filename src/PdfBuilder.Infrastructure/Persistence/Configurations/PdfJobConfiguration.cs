using Blocks.ServiceDefaults.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PdfBuilder.Contracts;
using PdfBuilder.Infrastructure.Persistence.Records;

namespace PdfBuilder.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>pdf_jobs</c>, as laid out in specs/001-persistent-storage/data-model.md.</summary>
internal static class PdfJobConfiguration
{
    public static void Configure(EntityTypeBuilder<PdfJobRecord> job)
    {
        // The three names PdfJobStatus has, as the API returns them. A typo in a writer fails here.
        job.ToTable("pdf_jobs", table =>
        {
            table.HasCheckConstraint(
                "ck_pdf_jobs_status", "status IN ('Pending', 'Succeeded', 'Failed')");

            // Retention is counted from the file's creation, so a succeeded job must say when that was.
            table.HasCheckConstraint(
                "ck_pdf_jobs_file_created_at", "status <> 'Succeeded' OR file_created_at IS NOT NULL");
        });

        job.HasKey(j => j.JobId);
        job.Property(j => j.JobId).ValueGeneratedNever();

        // Job ids stay unique across every client: logs, files and queued messages are keyed by the id.
        // The owner decides who can read the job; it is set when the job is created and never changes.
        job.Property(j => j.ClientId).HasMaxLength(40).IsRequired();
        job.HasClientOwner(j => j.ClientId, "fk_pdf_jobs_clients_client_id");

        // "≤ 64 chars": the template key limit published in PdfRequestLimits, validated before insert.
        job.Property(j => j.TemplateKey).HasMaxLength(PdfRequestLimits.TemplateKeyMaxLength).IsRequired();
        job.Property(j => j.Status).IsRequired();

        // Retention filters and the sweeper both look at this column.
        job.HasIndex(j => j.LastSavedAt).HasDatabaseName("ix_pdf_jobs_last_saved_at");

        // The file sweeper finds the jobs whose files are past retention by this column.
        job.HasIndex(j => j.FileCreatedAt).HasDatabaseName("ix_pdf_jobs_file_created_at");
    }
}
