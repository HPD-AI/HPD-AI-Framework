namespace HPD.Events.Struct;

/// <summary>Statistics for one struct event route.</summary>
public readonly record struct StructEventRouteStats(
    Type EventType,
    int SubscriberCount,
    int InboxCount,
    int CurrentQueued,
    int MaxQueued,
    long Emitted,
    long Accepted,
    long Dropped,
    long Filtered,
    long SubscriberWrites,
    long SubscriberDrops);

/// <summary>Aggregate statistics for all struct event routes.</summary>
public readonly record struct StructEventHubStats(
    int RouteCount,
    int SubscriberCount,
    int InboxCount,
    int CurrentQueued,
    int MaxQueued,
    long Emitted,
    long Accepted,
    long Dropped,
    long Filtered,
    long SubscriberWrites,
    long SubscriberDrops);
