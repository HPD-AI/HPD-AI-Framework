namespace HPD.Events;

/// <summary>Snapshot of current class-event channel depths.</summary>
public readonly record struct EventCoordinatorStats(
    int Streaming,
    int Synchronous,
    int Interactive,
    int Control);
