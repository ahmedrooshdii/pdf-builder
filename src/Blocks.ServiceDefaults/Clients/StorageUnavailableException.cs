namespace Blocks.ServiceDefaults.Clients;

/// <summary>
/// The storage behind <see cref="IClientKeyStore"/> cannot be reached right now; trying again in a few
/// seconds can succeed. Answered with <c>503 STORAGE_UNAVAILABLE</c>. The provider's own exception is
/// kept as <see cref="Exception.InnerException"/> for the logs and is never shown to a caller.
/// </summary>
public sealed class StorageUnavailableException : Exception
{
    public StorageUnavailableException()
        : base("The client and key storage cannot be reached.")
    {
    }

    public StorageUnavailableException(string message)
        : base(message)
    {
    }

    public StorageUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public StorageUnavailableException(Exception innerException)
        : base("The client and key storage cannot be reached.", innerException)
    {
    }
}
