using Blocks.ServiceDefaults.EntityFramework;
using Blocks.ServiceDefaults.Identity;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using PdfBuilder.Infrastructure.Persistence.Configurations;
using PdfBuilder.Infrastructure.Persistence.Records;

namespace PdfBuilder.Infrastructure.Persistence;

/// <summary>
/// The block's database: job status, authored templates, the record of the one-time template
/// import, clients and their API keys, console sessions, the Data Protection key ring that protects
/// their cookies, and MassTransit's outbox tables.
/// </summary>
/// <remarks>
/// Public because the composition root hands it to MassTransit's outbox; the record types are
/// internal, so everything that reads or writes them lives in this assembly, behind the ports in
/// <c>PdfBuilder.Application.Abstractions</c>.
/// </remarks>
public sealed class PdfBuilderDbContext(DbContextOptions<PdfBuilderDbContext> options, ICurrentClient caller)
    : DbContext(options)
{
    /// <summary>The name of the filter that confines templates and jobs to the caller's client.</summary>
    public const string ClientFilter = "client";

    // Read each time a query runs, not when the model is built: EF turns a member of the context into a
    // parameter, so one cached model serves every client, and a context created before a bus consumer
    // says which client it acts as still sees that client.
    private string? CallerClientId => caller.ClientId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<PdfJobRecord>(PdfJobConfiguration.Configure);
        modelBuilder.Entity<AuthoredTemplateRecord>(AuthoredTemplateConfiguration.Configure);

        // Templates and jobs belong to the client that created them, and a query sees only the caller's.
        // Fail closed: with no client (the operator who has not chosen one, or code outside a request) the
        // condition is false, so nothing matches, never everything. Written out rather than left to SQL
        // null semantics, because rows with no owner exist for a moment while an upgrade assigns them.
        modelBuilder.Entity<PdfJobRecord>().HasQueryFilter(ClientFilter, j => CallerClientId != null && j.ClientId == CallerClientId);
        modelBuilder.Entity<AuthoredTemplateRecord>().HasQueryFilter(ClientFilter, t => CallerClientId != null && t.ClientId == CallerClientId);

        modelBuilder.Entity<TemplateImportRecord>(TemplateImportConfiguration.Configure);

        // Clients and their API keys, defined by the shared authentication stud.
        modelBuilder.AddBlocksClients();

        // Console sign-ins and the key ring that protects their cookies, from the same stud.
        modelBuilder.AddBlocksConsoleSessions();

        // The transactional outbox: a job and the message that starts it are committed together.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
