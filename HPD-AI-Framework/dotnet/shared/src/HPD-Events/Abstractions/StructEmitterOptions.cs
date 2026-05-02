namespace HPD.Events;

/// <summary>
/// Hot-path emitter options for one local struct-event type.
/// </summary>
public sealed record StructEmitterOptions<TEvent>
    where TEvent : struct, IStructEvent
{
    /// <summary>Assign sequence numbers when TEvent implements ISequencedStructEvent&lt;TEvent&gt;.</summary>
    public bool AssignSequenceNumbers { get; init; }

    /// <summary>Optional hot-path filter. Returning false skips emission.</summary>
    public Func<TEvent, bool>? Filter { get; init; }
}
