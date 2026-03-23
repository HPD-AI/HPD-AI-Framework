namespace HPD.Events;

/// <summary>
/// Schedules events for future delivery (pull-based).
///
/// This is the strategy-friendly alternative to timer callbacks:
/// - Schedule events with Schedule()
/// - Pull them during the Host/Runner tick with TakeScheduledUpTo()
/// </summary>
public interface IEventScheduler
{
    /// <summary>Schedule event for delivery at absolute time.</summary>
    void Schedule(Event evt, DateTimeOffset deliveryTime);

    /// <summary>Schedule event for delivery after delay.</summary>
    void Schedule(Event evt, TimeSpan delay);

    /// <summary>Peek at events scheduled before given time (non-destructive).</summary>
    IEnumerable<(DateTimeOffset Time, Event Event)> PeekScheduledBefore(DateTimeOffset time);

    /// <summary>
    /// Remove and return all events scheduled up to given time.
    /// Call this during the Host/Runner tick to pull scheduled events.
    /// </summary>
    IReadOnlyList<Event> TakeScheduledUpTo(DateTimeOffset time);

    /// <summary>Next scheduled event time, if any.</summary>
    DateTimeOffset? NextScheduledTime { get; }

    /// <summary>Clear all scheduled events.</summary>
    void Clear();
}
