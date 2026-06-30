namespace HPD.Events;

/// <summary>
/// Request used to open one live class-event stream.
/// </summary>
/// <typeparam name="TEvent">Event type yielded by the stream.</typeparam>
public sealed record EventStreamRequest<TEvent>
    where TEvent : Event
{
    /// <summary>Optional logical stream identifier for diagnostics and projections.</summary>
    public string? StreamId { get; init; }

    /// <summary>Optional event channel filter.</summary>
    public EventChannel? Channel { get; init; }

    /// <summary>Per-stream inbox capacity.</summary>
    public int Capacity { get; init; } = 1024;

    /// <summary>Backpressure mode for the underlying inbox.</summary>
    public AsyncStreamBackpressureMode Backpressure { get; init; } =
        AsyncStreamBackpressureMode.Wait;

    /// <summary>Whether typed streams receive derived event types.</summary>
    public bool IncludeDerivedTypes { get; init; } = true;
}

/// <summary>
/// Source for live class-event streams backed by caller-owned inboxes.
/// </summary>
/// <typeparam name="TEvent">Event type yielded by the stream.</typeparam>
public interface IEventStreamSource<TEvent>
    : IAsyncStreamSource<EventStreamRequest<TEvent>, TEvent>
    where TEvent : Event;
