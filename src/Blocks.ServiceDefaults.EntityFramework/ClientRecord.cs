namespace Blocks.ServiceDefaults.EntityFramework;

/// <summary>A row of <c>clients</c>. Clients are never deleted: logs and, later, stored data refer to their ids.</summary>
internal sealed class ClientRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null while the client is active.</summary>
    public DateTimeOffset? DisabledAt { get; set; }
}
