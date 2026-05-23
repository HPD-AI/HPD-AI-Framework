namespace HPD.Events;

/// <summary>
/// Filters and limits for replay reads.
/// </summary>
/// <param name="From">Inclusive lower bound for event time.</param>
/// <param name="To">Exclusive upper bound for event time.</param>
/// <param name="EventFlowId">Optional event-flow ID filter.</param>
/// <param name="Limit">Optional maximum number of events to return.</param>
public sealed record ReplayReadOptions(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? EventFlowId,
    int? Limit)
{
    /// <summary>
    /// Options that read all events.
    /// </summary>
    public static ReplayReadOptions All { get; } = new(null, null, null, null);
}
