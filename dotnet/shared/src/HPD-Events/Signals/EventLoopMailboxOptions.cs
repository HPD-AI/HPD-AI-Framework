namespace HPD.Events.Signals;

/// <summary>Options for a local event-loop mailbox.</summary>
public sealed record EventLoopMailboxOptions
{
    /// <summary>Maximum number of queued items.</summary>
    public int Capacity { get; init; } = 1024;

    /// <summary>Behavior when the mailbox is full.</summary>
    public EventLoopMailboxOverflowMode OverflowMode { get; init; } =
        EventLoopMailboxOverflowMode.DropOldest;
}

/// <summary>Full-buffer policy for an event-loop mailbox.</summary>
public enum EventLoopMailboxOverflowMode
{
    /// <summary>Do not accept the write and report backpressure to the caller.</summary>
    Backpressure,

    /// <summary>Discard the oldest queued item and accept the new item.</summary>
    DropOldest,

    /// <summary>Discard the new item and keep the existing queue unchanged.</summary>
    DropNewest,

    /// <summary>Reject the write without treating it as a drop.</summary>
    Reject
}
