using MassTransit;
using Microsoft.EntityFrameworkCore;
using PdfBuilder.Infrastructure.Persistence.Configurations;
using PdfBuilder.Infrastructure.Persistence.Records;

namespace PdfBuilder.Infrastructure.Persistence;

/// <summary>
/// The block's database: job status, authored templates, the record of the one-time template
/// import, and MassTransit's outbox tables.
/// </summary>
/// <remarks>
/// Public because the composition root hands it to MassTransit's outbox; the record types are
/// internal, so everything that reads or writes them lives in this assembly, behind the ports in
/// <c>PdfBuilder.Application.Abstractions</c>.
/// </remarks>
public sealed class PdfBuilderDbContext(DbContextOptions<PdfBuilderDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<PdfJobRecord>(PdfJobConfiguration.Configure);
        modelBuilder.Entity<AuthoredTemplateRecord>(AuthoredTemplateConfiguration.Configure);
        modelBuilder.Entity<TemplateImportRecord>(TemplateImportConfiguration.Configure);

        // The transactional outbox: a job and the message that starts it are committed together.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
