namespace HPD.Events;

/// <summary>
/// Universal base class for all events in the HPD ecosystem.
/// All domain-specific events (Agent, Graph, Audio, etc.) inherit from this class.
/// Provides common fields for event classification, channel routing, and timestamps.
/// </summary>
public abstract record Event : IEvent
{
    /// <summary>
    /// Routing channel. Determines which independent channel this event travels through.
    /// Default: Synchronous.
    /// </summary>
    public virtual EventChannel Channel { get; init; } = EventChannel.Synchronous;

    /// <summary>
    /// Event classification (Lifecycle, Content, Control, Diagnostic).
    /// Used for filtering and routing events to appropriate handlers.
    /// Default: Content
    /// Override in derived event classes to provide domain-specific defaults.
    /// </summary>
    public virtual EventKind Kind { get; init; } = EventKind.Content;

    /// <summary>
    /// Direction of event flow (Downstream or Upstream).
    /// Downstream: normal flow from parent to child.
    /// Upstream: bubbling from child to parent (interruptions, cancellations).
    /// Default: Downstream
    /// </summary>
    public virtual EventDirection Direction { get; init; } = EventDirection.Downstream;

    /// <summary>
    /// Event flow ID for interruptible event flow grouping.
    /// Events with the same EventFlowId can be interrupted as a group.
    /// Null if event is not part of an interruptible event flow.
    /// </summary>
    public string? EventFlowId { get; init; }

    /// <summary>
    /// Whether this event can be dropped on event flow interruption.
    /// If true, event will be discarded when its event flow is interrupted.
    /// If false, event will be delivered even after interruption.
    /// Default: true
    /// </summary>
    public bool CanInterrupt { get; init; } = true;

    /// <summary>
    /// UTC timestamp when event was created.
    /// Automatically populated at event construction.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// External timestamp in nanoseconds since Unix epoch.
    /// Zero if not set.
    /// </summary>
    public long ExchangeTimestampNs { get; init; }

}
