namespace HPD.Events.Struct;

/// <summary>Extensions for constrained struct event route operations.</summary>
public static class StructEventRouteSequencingExtensions
{
    /// <summary>Create a sequenced emitter for routes whose event type supports sequencing.</summary>
    public static SequencedStructEventEmitter<TEvent> CreateSequencedEmitter<TEvent>(
        this StructEventRoute<TEvent> route,
        StructEventEmitterOptions<TEvent>? options = null)
        where TEvent : struct, IStructEvent, ISequencedStructEvent<TEvent>
    {
        ArgumentNullException.ThrowIfNull(route);
        return new SequencedStructEventEmitter<TEvent>(route, options);
    }
}
