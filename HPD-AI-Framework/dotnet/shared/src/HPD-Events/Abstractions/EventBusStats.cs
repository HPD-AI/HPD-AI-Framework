namespace HPD.Events;

/// <summary>
/// Current class-event bus health.
/// </summary>
public readonly record struct EventBusStats(
    int SubscriberCount,
    int InboxCount,
    int TotalQueued,
    int TotalDropped,
    int MaxSubscriberDepth);
