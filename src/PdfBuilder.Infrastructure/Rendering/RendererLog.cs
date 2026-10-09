using Microsoft.Extensions.Logging;

namespace PdfBuilder.Infrastructure.Rendering;

/// <summary>
/// Source-generated log methods for the renderer. Keeping them together makes the browser
/// lifecycle easy to follow in a log aggregator: launch, retire, disconnect.
/// </summary>
internal static partial class RendererLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Launched Chromium {Version}")]
    public static partial void BrowserLaunched(ILogger logger, string version);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Retiring Chromium (healthy: {IsHealthy}, served: {ServedRenders} renders)")]
    public static partial void BrowserRetiring(ILogger logger, bool isHealthy, int servedRenders);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Chromium disconnected unexpectedly")]
    public static partial void BrowserDisconnected(ILogger logger);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Debug,
        Message = "Ignoring error while closing the browser context")]
    public static partial void ContextCloseFailed(ILogger logger, Exception exception);
}
