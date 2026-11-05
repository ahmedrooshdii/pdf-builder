namespace PdfBuilder.Api.Messaging;

internal static partial class ConsumerLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Warning,
        Message = "PDF request rejected: {ErrorCode}")]
    public static partial void RequestRejected(ILogger logger, Exception exception, string errorCode);

    // The client id is not logged: for a malformed one it could be anything the publisher sent.
    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Warning,
        Message = "PDF request refused because of its client: {ErrorCode}")]
    public static partial void ClientRefused(ILogger logger, string errorCode);
}
