using Blocks.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PdfBuilder.Infrastructure.Persistence;

/// <summary>
/// Gives templates and jobs that were created before clients existed an owner, once, during the
/// upgrade to per-client data (spec FR-016, research R6 and R7).
/// </summary>
/// <remarks>
/// <para>
/// The owner is the client the operator names in <c>Blocks:Bus:DefaultClientId</c>. The service does
/// not guess: only the operator knows which product uses today's templates. With nothing to assign it
/// asks for nothing, so a fresh install needs no setting.
/// </para>
/// <para>
/// The client is registered here when it is missing, because every version before this one has no
/// clients, and a client can otherwise only be registered through the running service, which would not
/// start. It is one statement with <c>ON CONFLICT DO NOTHING</c>, in the caller's transaction: a second
/// instance, or an operator registering the same id at that moment, cannot make it fail.
/// </para>
/// <para>
/// This runs between two migrations: <c>AddClientOwner</c> adds a nullable owner column, this fills it,
/// and <c>RequireClientOwner</c> makes it required. The statements are raw SQL for that reason: the
/// current model already says the column is required, and the context's client filter would match
/// none of these rows.
/// </para>
/// </remarks>
internal sealed class ClientOwnerAssignment(
    IOptions<BlocksBusOptions> options,
    TimeProvider clock,
    ILogger<ClientOwnerAssignment> logger)
{
    // Any constant will do; it only has to be the same in every instance, and different from the
    // template import's.
    private const long AdvisoryLockKey = 0x5044_4642_4F57_4E31;

    internal const string MissingSetting =
        "Templates or jobs from before clients existed need an owner. Set Blocks:Bus:DefaultClientId " +
        "(environment variable Blocks__Bus__DefaultClientId) to the id of the client that uses them, " +
        "then start again. Nothing has been changed.";

    /// <summary>
    /// The owner for data from before clients existed, registering that client when it is not registered
    /// yet. A disabled client is used as it is: its data is kept and hidden, as for any disabled client.
    /// Runs in the transaction the caller already holds on <paramref name="db"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The setting is not set.</exception>
    public async Task<string> RequireOwnerAsync(PdfBuilderDbContext db, CancellationToken cancellationToken)
    {
        var clientId = options.Value.DefaultClientId ?? throw new InvalidOperationException(MissingSetting);
        var now = clock.GetUtcNow();

        var registered = await db.Database.ExecuteSqlAsync(
            $"INSERT INTO clients (id, name, created_at) VALUES ({clientId}, {clientId}, {now}) ON CONFLICT (id) DO NOTHING",
            cancellationToken);

        if (registered > 0)
            PersistenceLog.DefaultClientRegistered(logger, clientId);

        return clientId;
    }

    /// <summary>
    /// Every template and job without an owner becomes the default client's, in one transaction that
    /// holds an advisory lock, so however many instances start together it happens once. Does nothing,
    /// and does not read the setting, when every row already has an owner.
    /// </summary>
    public async Task AssignUnownedToDefaultClientAsync(PdfBuilderDbContext db, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Held until this transaction ends, which is also when the new owners become visible.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", cancellationToken);

        var unownedTemplates = await db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM authored_templates WHERE client_id IS NULL")
            .SingleAsync(cancellationToken);
        var unownedJobs = await db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pdf_jobs WHERE client_id IS NULL")
            .SingleAsync(cancellationToken);

        if (unownedTemplates + unownedJobs == 0)
            return;

        var owner = await RequireOwnerAsync(db, cancellationToken);

        var templates = await db.Database.ExecuteSqlAsync(
            $"UPDATE authored_templates SET client_id = {owner} WHERE client_id IS NULL", cancellationToken);
        var jobs = await db.Database.ExecuteSqlAsync(
            $"UPDATE pdf_jobs SET client_id = {owner} WHERE client_id IS NULL", cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        PersistenceLog.ClientOwnerAssigned(logger, templates, jobs, owner);
    }
}
