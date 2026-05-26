namespace HPD.Events;

/// <summary>
/// Process-local route registry for high-volume struct event lanes.
/// </summary>
public interface ILocalStructEventBus
{
    /// <summary>Get the typed local route for one struct event type.</summary>
    LocalStructEventRoute<TEvent> Route<TEvent>()
        where TEvent : struct, IStructEvent;

    /// <summary>Get aggregate local struct lane statistics.</summary>
    LocalStructEventBusStats GetStats();

    /// <summary>Get statistics for every known local struct event route.</summary>
    IReadOnlyList<LocalStructEventTypeStats> GetRouteStats();
}
