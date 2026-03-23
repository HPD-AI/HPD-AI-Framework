namespace HPD.Events;

/// <summary>
/// Priority level for event routing through channels.
/// Higher priority events are processed before lower priority events.
/// </summary>
public enum EventPriority
{
    /// <summary>
    /// Background processing (telemetry, metrics, non-critical logging).
    /// Lowest priority - processed when no other events are pending.
    /// </summary>
    Background = 0,

    /// <summary>
    /// Normal content and data flow (default).
    /// Standard priority for most events.
    /// </summary>
    Normal = 1,

    /// <summary>
    /// Control flow events (permissions, clarifications, user input).
    /// Higher priority than content to enable responsive interactions.
    /// </summary>
    Control = 2,

    /// <summary>
    /// Immediate processing (emergency stops, user cancellations).
    /// Highest priority for time-critical events.
    /// </summary>
    Immediate = 3,

    /// <summary>
    /// Upstream propagation (interruptions, cancellations flowing up hierarchy).
    /// Reserved for events that need to interrupt child coordinators.
    /// </summary>
    Upstream = 4
}
