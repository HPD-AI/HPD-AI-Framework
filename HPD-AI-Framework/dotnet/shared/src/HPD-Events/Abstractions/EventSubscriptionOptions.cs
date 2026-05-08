using System.Threading.Channels;

namespace HPD.Events;

/// <summary>
/// Delivery settings for one class-event subscriber.
/// </summary>
public sealed record EventSubscriptionOptions
{
    /// <summary>Per-subscriber mailbox capacity.</summary>
    public int Capacity { get; init; } = 1024;

    /// <summary>Backpressure behavior when this subscriber mailbox is full.</summary>
    public BoundedChannelFullMode FullMode { get; init; } = BoundedChannelFullMode.DropOldest;

    /// <summary>Whether typed subscriptions receive derived event types.</summary>
    public bool IncludeDerivedTypes { get; init; } = true;

    /// <summary>Optional channel filter for this subscription.</summary>
    public EventChannel? Channel { get; init; }
}
