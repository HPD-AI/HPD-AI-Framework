using Microsoft.Extensions.Logging;

namespace HPD.Base;

internal static partial class HPDBaseRealtimeLog
{
    /// <summary>Executes the event stream open failed operation.</summary>
    [LoggerMessage(
        EventId = 5000,
        EventName = "EventStreamOpenFailed",
        Level = LogLevel.Warning,
        Message = "The realtime event stream could not be opened ({ErrorCategory}, {ErrorCode}).")]
    public static partial void EventStreamOpenFailed(
        ILogger logger,
        string errorCategory,
        string errorCode);

    /// <summary>Executes the event projection failed operation.</summary>
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
