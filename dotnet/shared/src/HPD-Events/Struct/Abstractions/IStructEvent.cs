namespace HPD.Events.Struct;

using HPD.Events;

/// <summary>
/// Root contract for local zero-allocation struct events.
/// Struct events are process-local hot-path samples/frames, not semantic workflow events.
/// </summary>
public interface IStructEvent
{
    /// <summary>Event classification for observability.</summary>
    EventKind Kind { get; }

    /// <summary>Assigned by a struct emitter when sequencing is enabled. Zero if unset.</summary>
    long SequenceNumber { get; }

    /// <summary>Timestamp in nanoseconds since Unix epoch. Zero if unset.</summary>
    long TimestampNs { get; }
}
