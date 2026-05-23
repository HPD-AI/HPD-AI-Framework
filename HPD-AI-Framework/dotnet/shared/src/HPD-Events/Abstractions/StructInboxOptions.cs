using System.Threading.Channels;

namespace HPD.Events;

/// <summary>
/// Delivery settings for one caller-owned struct-event inbox.
/// </summary>
public sealed record StructInboxOptions
{
    /// <summary>Per-inbox mailbox capacity.</summary>
    public int Capacity { get; init; } = 1024;

    /// <summary>Backpressure behavior when this inbox is full.</summary>
    public BoundedChannelFullMode FullMode { get; init; } = BoundedChannelFullMode.Wait;

    internal StructSubscriptionOptions ToSubscriptionOptions() =>
        new()
        {
            Capacity = Capacity,
            FullMode = FullMode
        };
}
