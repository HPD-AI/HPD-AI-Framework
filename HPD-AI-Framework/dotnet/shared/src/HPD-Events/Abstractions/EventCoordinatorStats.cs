namespace HPD.Events;

/// <summary>Snapshot of current class-event bus health.</summary>
public readonly record struct EventCoordinatorStats(
    int SubscriberCount,
    int StreamSubscriberCount,
    int TotalQueued,
    int TotalDropped,
    int MaxSubscriberDepth);
