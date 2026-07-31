using Microsoft.Extensions.Logging;

namespace HPD.Base;

internal static partial class HPDBaseRealtimeLog
{
    [LoggerMessage(
        EventId = 5000,
        EventName = "EventStreamOpenFailed",
        Level = LogLevel.Warning,
        Message = "The realtime event stream could not be opened ({ErrorCategory}, {ErrorCode}).")]
    public static partial void EventStreamOpenFailed(
        ILogger logger,
        string errorCategory,
        string errorCode);

    [LoggerMessage(
        EventId = 5001,
        EventName = "EventProjectionFailed",
        Level = LogLevel.Warning,
        Message = "A realtime event could not be projected ({ErrorCategory}, {ErrorCode}).")]
    public static partial void EventProjectionFailed(
        ILogger logger,
        string errorCategory,
        string errorCode);
}
