namespace PdfBuilder.Api.Messaging;

internal static partial class ConsumerLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Warning,
        Message = "PDF request rejected: {ErrorCode}")]
    public static partial void RequestRejected(ILogger logger, Exception exception, string errorCode);
}
