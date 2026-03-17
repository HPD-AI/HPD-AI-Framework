namespace HPD.Events;

/// <summary>
/// Universal base class for all events in the HPD ecosystem.
/// All domain-specific events (Agent, Graph, Audio, etc.) inherit from this class.
/// Provides common fields for event classification, priority routing, timestamps, and cross-domain metadata.
/// </summary>
public abstract record Event
{
    /// <summary>
    /// Event classification (Lifecycle, Content, Control, Diagnostic).
    /// Used for filtering and routing events to appropriate handlers.
    /// Default: Content
    /// Override in derived event classes to provide domain-specific defaults.
    /// </summary>
    public virtual EventKind Kind { get; init; } = EventKind.Content;

    /// <summary>
    /// Priority for channel routing.
    /// Higher priority events are processed before lower priority events.
    /// Default: Normal
    /// Override in derived event classes to provide domain-specific defaults.
    /// </summary>
    public virtual EventPriority Priority { get; init; } = EventPriority.Normal;

    /// <summary>
    /// Direction of event flow (Downstream or Upstream).
    /// Downstream: normal flow from parent to child.
    /// Upstream: bubbling from child to parent (interruptions, cancellations).
    /// Default: Downstream
    /// </summary>
    public EventDirection Direction { get; init; } = EventDirection.Downstream;

    /// <summary>
    /// Sequence number for ordering (assigned by coordinator).
    /// Automatically incremented by the event coordinator to maintain event order.
    /// </summary>
    public long SequenceNumber { get; set; }

    /// <summary>
    /// Stream ID for interruptible stream grouping.
    /// Events with the same StreamId can be interrupted as a group.
    /// Null if event is not part of an interruptible stream.
    /// </summary>
    public string? StreamId { get; init; }

    /// <summary>
    /// Whether this event can be dropped on stream interruption.
    /// If true, event will be discarded when its stream is interrupted.
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
    /// Optional extension dictionary for cross-domain metadata.
    ///
    /// Prefer using domain-specific typed fields (e.g., AgentEvent.ExecutionContext).
    /// Use this only for advanced scenarios requiring cross-domain context enrichment.
    ///
    /// Example: Graph coordinator enriching agent events with graph execution context.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Extensions { get; init; }
}
