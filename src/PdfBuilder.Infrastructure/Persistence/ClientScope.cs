using Blocks.ServiceDefaults.Identity;

namespace PdfBuilder.Infrastructure.Persistence;

/// <summary>
/// The owner to write on a new row. A query filter does not apply to inserts, so the stores ask for
/// the owner here, which keeps it visible in code review (research R3).
/// </summary>
public sealed class ClientScope(ICurrentClient caller)
{
    /// <exception cref="ClientRequiredException">The caller has no client.</exception>
    public string RequireClientId() => caller.ClientId ?? throw new ClientRequiredException();
}
