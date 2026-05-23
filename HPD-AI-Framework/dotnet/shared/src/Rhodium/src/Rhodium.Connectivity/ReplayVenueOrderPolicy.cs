using Rhodium.Primitives;

namespace Rhodium.Connectivity;

/// <summary>
/// Venue-level order admission rules for deterministic replay.
/// This models broker/exchange "will the venue accept this order shape" behavior
/// before replay matching, risk, and account checks run.
/// </summary>
public sealed record ReplayVenueOrderPolicy
{
    public static readonly ReplayVenueOrderPolicy Default = new();

    public IReadOnlySet<OrderType>? AllowedOrderTypes { get; init; }
    public IReadOnlySet<TimeInForce>? AllowedTimeInForce { get; init; }
    public bool AllowPostOnly { get; init; } = true;
    public Qty? MinOrderQuantity { get; init; }
    public Money? MinOrderNotional { get; init; }
}
