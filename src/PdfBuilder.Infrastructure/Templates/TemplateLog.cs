using Microsoft.Extensions.Logging;

namespace PdfBuilder.Infrastructure.Templates;

internal static partial class TemplateLog
{
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Warning,
        Message = "Ignoring malformed render options for template {TemplateKey}; falling back to defaults")]
    public static partial void MalformedRenderOptions(ILogger logger, Exception exception, string templateKey);
}
