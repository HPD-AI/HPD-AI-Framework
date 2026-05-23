namespace HPD.Events;

/// <summary>
/// Reads historical or synthetic events for deterministic replay.
/// </summary>
/// <typeparam name="TEvent">Event type produced by the replay source.</typeparam>
public interface IReplaySource<out TEvent>
    where TEvent : Event
{
    /// <summary>
    /// Read events from this source in its natural source order.
    /// </summary>
    IAsyncEnumerable<TEvent> ReadAsync(
        ReplayReadOptions options,
        CancellationToken ct = default);
}
