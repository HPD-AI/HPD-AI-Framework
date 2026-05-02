namespace HPD.Events;

/// <summary>
/// Routing channel for an event. Choose based on event behavior, not urgency.
/// </summary>
public enum EventChannel
{
    /// <summary>High-throughput data where old items can be skipped or coalesced.</summary>
    Streaming,

    /// <summary>Causally ordered data that must be processed FIFO.</summary>
    Synchronous,

    /// <summary>User-facing interactions that must remain responsive and ordered.</summary>
    Interactive,

    /// <summary>Interruptions, cancellations, and circuit-breaker signals.</summary>
    Control
}
