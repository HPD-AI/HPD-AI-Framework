namespace HPD.Events.Struct;

/// <summary>
/// Process-local route registry for high-volume struct event lanes.
/// </summary>
public interface IStructEventHub
{
    /// <summary>Get the typed route for one struct event type.</summary>
    StructEventRoute<TEvent> Route<TEvent>(
        StructEventRouteOptions? options = null)
        where TEvent : struct, IStructEvent;

    /// <summary>Get aggregate struct event lane statistics.</summary>
    StructEventHubStats GetStats();

    /// <summary>Get statistics for every known struct event event route.</summary>
    IReadOnlyList<StructEventRouteStats> GetRouteStats();
}
