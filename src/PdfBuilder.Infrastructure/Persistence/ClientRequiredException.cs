namespace PdfBuilder.Infrastructure.Persistence;

/// <summary>
/// A write needed an owner and the caller has no client: the operator who has not chosen one, or code
/// running outside a request. The HTTP endpoints refuse these calls earlier with the same message;
/// this is the second line, for a path that does not go through them.
/// </summary>
public sealed class ClientRequiredException()
    : InvalidOperationException("Choose a client: this operation needs a client to act as.");
