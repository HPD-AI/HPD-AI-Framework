namespace HPD.Events;

/// <summary>Statistics for one local struct event route.</summary>
public readonly record struct LocalStructEventTypeStats(
    Type EventType,
    int SubscriberCount,
    int InboxCount,
    int ObserverCount,
    int CurrentQueued,
    int MaxQueued,
    long Emitted,
    long Accepted,
    long Dropped,
    long Filtered,
    long SubscriberWrites,
    long SubscriberDrops);

/// <summary>Aggregate statistics for all local struct event routes.</summary>
public readonly record struct LocalStructEventBusStats(
    int RouteCount,
    int SubscriberCount,
    int InboxCount,
    int ObserverCount,
    int CurrentQueued,
    int MaxQueued,
    long Emitted,
    long Accepted,
    long Dropped,
    long Filtered,
    long SubscriberWrites,
    long SubscriberDrops);
