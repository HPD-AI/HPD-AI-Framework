namespace HPD.Events.Signals;

/// <summary>Options for a local event-loop wake signal.</summary>
public sealed record EventSignalOptions
{
    /// <summary>Wake accounting behavior for this signal.</summary>
    public EventSignalMode Mode { get; init; } = EventSignalMode.Coalescing;
}

/// <summary>Wake accounting behavior for an event signal.</summary>
public enum EventSignalMode
{
    /// <summary>Preserve at most one pending wake when no waiter is present.</summary>
    Coalescing,

    /// <summary>Preserve a count of pending wakes when no waiter is present.</summary>
    Counting
}
