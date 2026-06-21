using System.Threading.Channels;

namespace HPD.Events;

/// <summary>
/// Delivery settings for one caller-owned class-event inbox.
/// </summary>
public sealed record EventInboxOptions
{
    /// <summary>Per-inbox mailbox capacity.</summary>
    public int Capacity { get; init; } = 1024;

    /// <summary>Backpressure behavior when this inbox is full.</summary>
    public BoundedChannelFullMode FullMode { get; init; } = BoundedChannelFullMode.Wait;

    /// <summary>Whether typed inboxes receive derived event types.</summary>
    public bool IncludeDerivedTypes { get; init; } = true;

    /// <summary>Optional channel filter for this inbox.</summary>
    public EventChannel? Channel { get; init; }

    /// <summary>
    /// Deterministic inbox defaults for primary event lanes.
    /// </summary>
    public static EventInboxOptions Deterministic(int capacity = 4096) =>
        new()
        {
            Capacity = capacity,
            FullMode = BoundedChannelFullMode.Wait,
            IncludeDerivedTypes = true
        };

    /// <summary>
    /// Latest-only inbox defaults for UI/status lanes.
    /// </summary>
    public static EventInboxOptions LatestOnly(int capacity = 1) =>
        new()
        {
            Capacity = capacity,
            FullMode = BoundedChannelFullMode.DropOldest,
            IncludeDerivedTypes = true
        };

    /// <summary>
    /// Lossy telemetry inbox defaults for non-critical event lanes.
    /// </summary>
    public static EventInboxOptions LossyTelemetry(int capacity = 1024) =>
        new()
        {
            Capacity = capacity,
            FullMode = BoundedChannelFullMode.DropWrite,
            IncludeDerivedTypes = true
        };

    internal EventSubscriptionOptions ToSubscriptionOptions() =>
        new()
        {
            Capacity = Capacity,
            FullMode = FullMode,
            IncludeDerivedTypes = IncludeDerivedTypes,
            Channel = Channel
        };
}
