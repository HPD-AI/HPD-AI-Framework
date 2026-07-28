using Microsoft.Extensions.Logging;

namespace HPD.Base.Realtime.AspNetCore.Observability.Logging;

internal static partial class HPDBaseRealtimeAspNetCoreLog
{
    [LoggerMessage(EventId = 5500, EventName = "WebSocketJoinRejectedProtocol", Level = LogLevel.Information,
        Message = "A realtime channel join was rejected by protocol or configuration ({ErrorCode}).")]
    public static partial void WebSocketJoinRejectedProtocol(ILogger logger, string errorCode);

    [LoggerMessage(EventId = 5501, EventName = "WebSocketSendFailed", Level = LogLevel.Warning,
        Message = "A realtime WebSocket send failed ({ErrorCategory}, {ErrorCode}).")]
    public static partial void WebSocketSendFailed(ILogger logger, string errorCategory, string errorCode);

    [LoggerMessage(EventId = 5502, EventName = "HeartbeatTimedOut", Level = LogLevel.Information,
        Message = "A realtime WebSocket connection timed out waiting for a heartbeat ({ErrorCode}).")]
    public static partial void HeartbeatTimedOut(ILogger logger, string errorCode);

    [LoggerMessage(EventId = 5503, EventName = "PayloadDropped", Level = LogLevel.Information,
        Message = "A realtime protocol payload was dropped ({ErrorCode}, {PayloadSizeBucket}).")]
    public static partial void PayloadDropped(ILogger logger, string errorCode, string payloadSizeBucket);

    [LoggerMessage(EventId = 5504, EventName = "ProtocolMessageUnsupported", Level = LogLevel.Debug,
        Message = "A realtime protocol message was rejected ({ProtocolMessageKind}, {ErrorCode}).")]
    public static partial void ProtocolMessageUnsupported(
        ILogger logger,
        string protocolMessageKind,
        string errorCode);

    [LoggerMessage(EventId = 5505, EventName = "WebSocketReceiveFailed", Level = LogLevel.Warning,
        Message = "A realtime WebSocket receive or channel pump failed ({ErrorCategory}, {ErrorCode}).")]
    public static partial void WebSocketReceiveFailed(ILogger logger, string errorCategory, string errorCode);

    [LoggerMessage(EventId = 5506, EventName = "ConnectionOpened", Level = LogLevel.Debug,
        Message = "A realtime WebSocket connection opened.")]
    public static partial void ConnectionOpened(ILogger logger);

    [LoggerMessage(EventId = 5508, EventName = "WebSocketJoinRejectedPolicy", Level = LogLevel.Debug,
        Message = "A realtime WebSocket channel join was rejected by policy ({ErrorCode}).")]
    public static partial void WebSocketJoinRejectedPolicy(ILogger logger, string errorCode);

    [LoggerMessage(EventId = 5509, EventName = "WebSocketConnectionRejected", Level = LogLevel.Information,
        Message = "A realtime WebSocket connection was rejected ({ErrorCode}).")]
    public static partial void WebSocketConnectionRejected(ILogger logger, string errorCode);
}
