namespace HPD.Events.Signals;

/// <summary>Current health counters for an event-loop mailbox.</summary>
public readonly record struct EventLoopMailboxStats(
    int Capacity,
    int Count,
    long Written,
    long Read,
    long Dropped,
    long Rejected,
    long Signaled,
    bool IsDisposed);
