using System.Net.Sockets;
using Npgsql;

namespace PdfBuilder.Infrastructure.Persistence;

/// <summary>Tells "the database cannot be reached right now" apart from every other failure.</summary>
public static class StorageFailures
{
    /// <summary>The <c>code</c> a caller receives, in a 503 problem, when storage is unavailable.</summary>
    public const string UnavailableCode = "STORAGE_UNAVAILABLE";

    /// <summary>
    /// True when <paramref name="exception"/>, or anything it wraps, means the connection failed or
    /// the server is shutting down or overloaded: retrying in a few seconds can succeed. A wrong
    /// password, a missing database or a bad query are not like that and stay ordinary failures.
    /// </summary>
    public static bool IsUnavailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is NpgsqlException { IsTransient: true } or SocketException or TimeoutException)
                return true;
        }

        return false;
    }
}
