namespace HPD.Events;

/// <summary>
/// Replay source that can append events before reading them back.
/// </summary>
/// <typeparam name="TEvent">Event type stored by this event store.</typeparam>
public interface IEventStore<TEvent> : IReplaySource<TEvent>
    where TEvent : Event
{
    /// <summary>
    /// Append an event to the store.
    /// </summary>
    ValueTask AppendAsync(TEvent evt, CancellationToken ct = default);
}
