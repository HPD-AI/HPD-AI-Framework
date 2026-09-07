using System.Threading.Channels;

namespace HPD.Events;

/// <summary>
/// Delivery settings for one class-event subscriber.
/// </summary>
public sealed record EventSubscriptionOptions
{
    /// <summary>
    /// Controls whether the subscriber accepts events from other coordinator owners. The default
    /// is <see cref="EventOwnerScope.SameOwner"/>; global infrastructure observers must explicitly
    /// select <see cref="EventOwnerScope.AllOwners"/>.
    /// </summary>
    public EventOwnerScope OwnerScope { get; init; } = EventOwnerScope.SameOwner;

    internal IEventDeliveryPolicy? DeliveryPolicy { get; init; }
    /// <summary>Per-subscriber mailbox capacity.</summary>
    public int Capacity { get; init; } = 1024;

    /// <summary>Backpressure behavior when this subscriber mailbox is full.</summary>
    public BoundedChannelFullMode FullMode { get; init; } = BoundedChannelFullMode.DropOldest;

    /// <summary>Whether typed subscriptions receive derived event types.</summary>
    public bool IncludeDerivedTypes { get; init; } = true;

    /// <summary>Optional channel filter for this subscription.</summary>
    public EventChannel? Channel { get; init; }
}
