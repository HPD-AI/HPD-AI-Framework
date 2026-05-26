using Rhodium.Primitives;

namespace Rhodium.Simulation;

/// <summary>
/// Venue or instrument scoped order admission policy for simulated exchanges.
/// This answers whether an exchange accepts an order shape before matching,
/// account reservation, latency-visible fills, or strategy callbacks.
/// </summary>
public sealed record SimulationOrderPolicy
{
    /// <summary>Default order admission policy.</summary>
    public static readonly SimulationOrderPolicy Default = new();

    /// <summary>Accepted order types. Null means all simulator-supported order types are allowed.</summary>
    public IReadOnlySet<OrderType>? AllowedOrderTypes { get; init; }

    /// <summary>Accepted time-in-force values. Null means all simulator-supported values are allowed.</summary>
    public IReadOnlySet<TimeInForce>? AllowedTimeInForce { get; init; }

    /// <summary>Whether post-only orders may be accepted.</summary>
    public bool AllowPostOnly { get; init; } = true;

    /// <summary>Minimum accepted order quantity.</summary>
    public Qty? MinOrderQuantity { get; init; }

    /// <summary>Minimum accepted order notional.</summary>
    public Money? MinOrderNotional { get; init; }
}
