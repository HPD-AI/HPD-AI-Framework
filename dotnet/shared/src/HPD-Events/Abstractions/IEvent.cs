namespace HPD.Events;

/// <summary>
/// Root contract for class events in the HPD ecosystem.
/// </summary>
public interface IEvent
{
    /// <summary>Routing channel used by the event coordinator.</summary>
    EventChannel Channel { get; }

    /// <summary>Event classification for filtering, diagnostics, and observers.</summary>
    EventKind Kind { get; }

    /// <summary>External high-resolution timestamp in nanoseconds since Unix epoch. Zero when unset.</summary>
    long ExchangeTimestampNs { get; }
}
