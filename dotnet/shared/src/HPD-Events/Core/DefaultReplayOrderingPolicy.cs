namespace HPD.Events.Core;

/// <summary>
/// Domain-agnostic replay ordering policy for HPD events.
/// </summary>
/// <typeparam name="TEvent">Event type ordered by this policy.</typeparam>
public sealed class DefaultReplayOrderingPolicy<TEvent> : IReplayOrderingPolicy<TEvent>
    where TEvent : Event
{
    /// <summary>
    /// Singleton default policy instance.
    /// </summary>
    public static DefaultReplayOrderingPolicy<TEvent> Instance { get; } = new();

    private DefaultReplayOrderingPolicy()
    {
    }

    /// <inheritdoc />
    public ReplayKey GetKey(TEvent evt, ReplaySourceInfo source, long sourceSequence)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(source);

        return new ReplayKey(
            ReplayTime.GetTimestampNs(evt),
            source.Priority,
            0,
            source.SourceOrdinal,
            sourceSequence);
    }
}
