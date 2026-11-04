using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Blocks.ServiceDefaults.Tests.Support;

/// <summary>One log line as written: its level, its text, its exception and the scope values around it.</summary>
public sealed record CapturedLog(
    string Category,
    LogLevel Level,
    string Message,
    string? Exception,
    IReadOnlyDictionary<string, object?> Scope);

/// <summary>Keeps every log line, with the values of the scopes it was written in.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyCollection<CapturedLog> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    /// <summary>All text written: messages, exceptions and scope values, for searching for a secret.</summary>
    public string AllText() => string.Join(
        Environment.NewLine,
        _entries.Select(e => $"{e.Message} {e.Exception} {string.Join(" ", e.Scope.Select(kv => $"{kv.Key}={kv.Value}"))}"));

    private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scope = new Dictionary<string, object?>(StringComparer.Ordinal);
            owner._scopes.ForEachScope(
                (value, values) =>
                {
                    if (value is IEnumerable<KeyValuePair<string, object>> pairs)
                    {
                        foreach (var (key, item) in pairs)
                            values[key] = item;
                    }
                    else if (value is not null)
                    {
                        values[$"#{values.Count}"] = value.ToString();
                    }
                },
                scope);

            owner._entries.Enqueue(new CapturedLog(category, logLevel, formatter(state, exception), exception?.ToString(), scope));
        }
    }
}
