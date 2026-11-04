using System.Net.Sockets;
using Npgsql;

namespace Blocks.ServiceDefaults.EntityFramework;

/// <summary>Tells "PostgreSQL cannot be reached right now" apart from every other failure.</summary>
public static class NpgsqlStorageFailures
{
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
