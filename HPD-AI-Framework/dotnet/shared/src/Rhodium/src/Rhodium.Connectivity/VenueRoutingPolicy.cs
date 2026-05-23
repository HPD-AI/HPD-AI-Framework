using Rhodium.Primitives;

namespace Rhodium.Connectivity;

/// <summary>
/// Venue-level controls for host-managed cross-venue market routing.
/// Explicit orders to a venue are still submitted to that venue; this policy
/// governs only opt-in best-venue and sweep routing.
/// </summary>
public sealed record VenueRoutingPolicy
{
    public static readonly VenueRoutingPolicy Default = new();

    public bool AllowBestVenueMarketRouting { get; init; } = true;
    public bool AllowMarketSweepRouting { get; init; } = true;
    public IReadOnlySet<TimeInForce>? AllowedMarketTimeInForce { get; init; }
    public Qty? MinMarketRoutingQuantity { get; init; }
    public Money? MinMarketRoutingNotional { get; init; }
    public Qty? MaxMarketSweepQuantity { get; init; }
}
