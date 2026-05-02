using System.Threading.Channels;

namespace HPD.Events;

/// <summary>
/// Backpressure settings for one local struct-event subscriber.
/// </summary>
public sealed record StructSubscriptionOptions
{
    /// <summary>Per-subscriber capacity. Bounded by default to prevent memory leaks.</summary>
    public int Capacity { get; init; } = 1024;

    /// <summary>Backpressure behavior when this subscriber is full.</summary>
    public BoundedChannelFullMode FullMode { get; init; } = BoundedChannelFullMode.DropOldest;
}
