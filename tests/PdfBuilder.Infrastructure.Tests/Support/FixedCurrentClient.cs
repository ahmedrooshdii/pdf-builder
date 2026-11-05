using Blocks.ServiceDefaults.Identity;

namespace PdfBuilder.TestSupport;

/// <summary>
/// An <see cref="ICurrentClient"/> a test sets by hand, so a store test can be one client, then
/// another, on the same <c>DbContext</c>. It is also the <see cref="IClientContext"/> a consumer acts
/// through, for tests that build their own service provider. Linked into every test project that
/// builds a context.
/// </summary>
public sealed class FixedCurrentClient(string? clientId = null) : ICurrentClient, IClientContext
{
    /// <summary>Change it between calls: the context's filter reads it each time a query runs.</summary>
    public string? ClientId { get; set; } = clientId;

    public string? KeyId => null;

    public IReadOnlySet<string> Scopes { get; } = new HashSet<string>(StringComparer.Ordinal);

    public bool IsAdmin => false;

    public void ActAs(string clientId) => ClientId = clientId;
}
