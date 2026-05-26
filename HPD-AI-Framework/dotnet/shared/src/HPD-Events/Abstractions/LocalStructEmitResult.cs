namespace HPD.Events;

/// <summary>Aggregate outcome for one local struct event emission.</summary>
public enum LocalStructEmitStatus
{
    /// <summary>At least one subscriber accepted the event.</summary>
    Accepted,

    /// <summary>The route had no subscribers when the event was emitted.</summary>
    NoSubscribers,

    /// <summary>The emitter filter skipped the event.</summary>
    Filtered,

    /// <summary>The event was dropped by subscriber full-buffer policy.</summary>
    Dropped,

    /// <summary>A subscriber requested backpressure instead of accepting the event.</summary>
    Backpressured,

    /// <summary>A subscriber rejected the event because its buffer was full.</summary>
    Rejected,

    /// <summary>The route or bus was disposed before the event could be emitted.</summary>
    Disposed
}

/// <summary>Aggregate fan-out result for one local struct event emission.</summary>
public readonly record struct LocalStructEmitResult(
    LocalStructEmitStatus Status,
    int SubscriberCount,
    int AcceptedCount,
    int DroppedCount)
{
    /// <summary>True when at least one subscriber accepted the event.</summary>
    public bool Accepted => AcceptedCount > 0;
}

/// <summary>Aggregate result for a batch of local struct event emissions.</summary>
public readonly record struct LocalStructEmitBatchResult(
    int EventCount,
    int AcceptedEvents,
    int DroppedEvents,
    int BackpressuredEvents,
    int RejectedEvents,
    int FilteredEvents,
    int TotalSubscriberWrites,
    int TotalSubscriberDrops);
