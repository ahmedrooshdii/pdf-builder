namespace Blocks.ServiceDefaults.Identity;

/// <summary>
/// The caller of this scope: whoever the HTTP request authenticated, unless the scope was told to act
/// as a client with <see cref="IClientContext.ActAs"/>, as a bus consumer is.
/// </summary>
/// <remarks>
/// Every member reads its state when it is called and caches nothing, so an object that received
/// <see cref="ICurrentClient"/> in its constructor, such as a <c>DbContext</c> resolved before
/// <c>ActAs</c> ran, still sees the client that was set afterwards.
/// </remarks>
internal sealed class CurrentClient(HttpCurrentClient http) : ICurrentClient, IClientContext
{
    private string? _actingAs;

    public string? ClientId => _actingAs ?? http.ClientId;

    public string? KeyId => _actingAs is null ? http.KeyId : null;

    public IReadOnlySet<string> Scopes => _actingAs is null ? http.Scopes : EmptyScopes;

    public bool IsAdmin => _actingAs is null && http.IsAdmin;

    public void ActAs(string clientId)
    {
        if (!HttpCurrentClient.IsClientId(clientId))
            throw new ArgumentException("Not a client id.", nameof(clientId));

        if (_actingAs is not null && !string.Equals(_actingAs, clientId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"This scope already acts as client '{_actingAs}'; it cannot also act as '{clientId}'.");

        _actingAs = clientId;
    }

    private static readonly HashSet<string> EmptyScopes = new(StringComparer.Ordinal);
}
