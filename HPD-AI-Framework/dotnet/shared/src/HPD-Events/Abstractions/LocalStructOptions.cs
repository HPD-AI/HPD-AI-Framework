namespace HPD.Events;

/// <summary>Options for a route-bound local struct emitter.</summary>
public sealed record LocalStructEmitterOptions<TEvent>
    where TEvent : struct, IStructEvent
{
    /// <summary>Optional synchronous filter. Returning false skips emission.</summary>
    public Func<TEvent, bool>? Filter { get; init; }
}

/// <summary>Options for a caller-owned local struct inbox.</summary>
public sealed record LocalStructInboxOptions
{
    /// <summary>Maximum number of queued events.</summary>
    public int Capacity { get; init; } = 1024;

    /// <summary>Behavior when the inbox is full.</summary>
    public LocalStructFullMode FullMode { get; init; } =
        LocalStructFullMode.Backpressure;
}

/// <summary>Options for a direct local struct subscription.</summary>
public sealed record LocalStructSubscriptionOptions
{
    /// <summary>Maximum number of queued events.</summary>
    public int Capacity { get; init; } = 1024;

    /// <summary>Behavior when the subscription is full.</summary>
    public LocalStructFullMode FullMode { get; init; } =
        LocalStructFullMode.DropOldest;
}

/// <summary>Full-buffer policy for local struct inboxes and subscriptions.</summary>
public enum LocalStructFullMode
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
