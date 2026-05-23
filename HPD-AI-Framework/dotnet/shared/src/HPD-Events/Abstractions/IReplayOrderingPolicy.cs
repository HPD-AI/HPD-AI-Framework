namespace HPD.Events;

/// <summary>
/// Creates deterministic ordering keys for replay events.
/// </summary>
/// <typeparam name="TEvent">Event type ordered by this policy.</typeparam>
public interface IReplayOrderingPolicy<in TEvent>
    where TEvent : Event
{
    /// <summary>
    /// Create the replay key for an event read from a source.
    /// </summary>
    ReplayKey GetKey(
        TEvent evt,
        ReplaySourceInfo source,
        long sourceSequence);
}
