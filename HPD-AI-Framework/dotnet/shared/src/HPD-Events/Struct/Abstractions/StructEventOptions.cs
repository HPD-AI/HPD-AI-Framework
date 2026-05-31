namespace HPD.Events.Struct;

/// <summary>Options for one struct event route.</summary>
public sealed record StructEventRouteOptions
{
    /// <summary>Concurrency shape requested for the route implementation.</summary>
    public StructEventConcurrencyMode ConcurrencyMode { get; init; } =
        StructEventConcurrencyMode.MultiProducerMultiConsumer;

    /// <summary>Amount of per-emit route statistics to maintain.</summary>
    public StructEventStatsMode StatsMode { get; init; } =
        StructEventStatsMode.Minimal;
}

/// <summary>Options for a route-bound struct event emitter.</summary>
public sealed record StructEventEmitterOptions<TEvent>
    where TEvent : struct, IStructEvent
{
    /// <summary>Optional synchronous filter. Returning false skips emission.</summary>
    public Func<TEvent, bool>? Filter { get; init; }
}

/// <summary>Options for a caller-owned struct event inbox.</summary>
public sealed record StructEventInboxOptions
{
    /// <summary>Maximum number of queued events.</summary>
    public int Capacity { get; init; } = 1024;

    /// <summary>Behavior when the inbox is full.</summary>
    public StructEventOverflowMode OverflowMode { get; init; } =
        StructEventOverflowMode.Backpressure;
}

/// <summary>Options for a direct struct event subscription.</summary>
public sealed record StructEventSubscriptionOptions
{
    /// <summary>Maximum number of queued events.</summary>
    public int Capacity { get; init; } = 1024;

    /// <summary>Behavior when the subscription is full.</summary>
    public StructEventOverflowMode OverflowMode { get; init; } =
        StructEventOverflowMode.Backpressure;
}

/// <summary>Requested concurrency shape for one struct event route.</summary>
public enum StructEventConcurrencyMode
{
    /// <summary>One producer and one consumer registration.</summary>
    SingleProducerSingleConsumer,

    /// <summary>Multiple producers and one consumer registration.</summary>
    MultiProducerSingleConsumer,

    /// <summary>Multiple producers and multiple consumer registrations.</summary>
    MultiProducerMultiConsumer
}

/// <summary>Full-buffer policy for struct event inboxes and subscriptions.</summary>
public enum StructEventOverflowMode
{
    /// <summary>Do not accept the write and report backpressure to the emitter.</summary>
    Backpressure,

    /// <summary>Discard the oldest queued event and accept the new event.</summary>
    DropOldest,

    /// <summary>Discard the new event and keep the existing queue unchanged.</summary>
    DropNewest,

    /// <summary>Reject the write without treating it as a drop.</summary>
    Reject
}

/// <summary>Statistics detail level for one struct event route.</summary>
public enum StructEventStatsMode
{
    /// <summary>Skip route-level statistics counters in the emit/read hot path.</summary>
    None,

    /// <summary>Maintain the default route-level statistics counters.</summary>
    Minimal,

    /// <summary>Reserved for richer route-level statistics.</summary>
    Full
}
