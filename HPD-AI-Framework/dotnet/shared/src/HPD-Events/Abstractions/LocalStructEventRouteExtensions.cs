namespace HPD.Events;

/// <summary>Extensions for constrained local struct route operations.</summary>
public static class LocalStructEventRouteExtensions
{
    /// <summary>Create a sequenced emitter for routes whose event type supports sequencing.</summary>
    public static LocalSequencedStructEmitter<TEvent> CreateSequencedEmitter<TEvent>(
        this LocalStructEventRoute<TEvent> route,
        LocalStructEmitterOptions<TEvent>? options = null)
        where TEvent : struct, IStructEvent, ISequencedStructEvent<TEvent>
    {
        ArgumentNullException.ThrowIfNull(route);
        return new LocalSequencedStructEmitter<TEvent>(route, options);
    }
}
