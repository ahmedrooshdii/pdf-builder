namespace Blocks.ServiceDefaults.Identity;

/// <summary>
/// Lets work that does not come from an HTTP request, such as a bus message, say which client it is
/// for. Code that reads identity still reads it only through <see cref="ICurrentClient"/>
/// (constitution Principle VI); this is how the consumer sets what that interface answers.
/// </summary>
/// <remarks>Call it before any other work in the scope: it must come first.</remarks>
public interface IClientContext
{
    /// <summary>
    /// From now on in this scope, <see cref="ICurrentClient.ClientId"/> is <paramref name="clientId"/>,
    /// and the caller holds no permission and is not the operator.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="clientId"/> is not a client id.</exception>
    /// <exception cref="InvalidOperationException">A different client was already set in this scope.</exception>
    void ActAs(string clientId);
}
