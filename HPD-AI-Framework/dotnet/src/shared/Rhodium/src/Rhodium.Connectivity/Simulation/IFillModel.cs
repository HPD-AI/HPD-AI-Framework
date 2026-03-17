using Rhodium.HFT;
using Rhodium.Primitives;

namespace Rhodium.Connectivity.Simulation;

/// <summary>
/// Controls fill simulation logic for limit orders.
/// Allows custom fill behavior without modifying core simulation.
/// </summary>
public interface IFillModel
{
    /// <summary>
    /// Determines if a limit order should fill based on market state.
    /// Called once per virtual index during fill checks (not batched).
    /// </summary>
    /// <returns>True if order should fill, false otherwise.</returns>
    bool ShouldFillLimit(ref FillContext ctx);

    /// <summary>
    /// Adjusts fill price based on custom logic (e.g., size-aware slippage).
    /// Called after fill is determined, before OrderFilled event is emitted.
    /// </summary>
    /// <returns>Adjusted fill price (can return ctx.NominalFillPrice for no adjustment).</returns>
    Price AdjustFillPrice(ref FillContext ctx);
}

/// <summary>
/// Context for fill decision (stack-allocated, zero-heap).
/// Contains all information needed to make fill decision.
/// </summary>
public readonly ref struct FillContext
{
    public required long OrderPriceTick { get; init; }
    public required long? BestBidTick { get; init; }
    public required long? BestAskTick { get; init; }
    public required double QueueRelativePosition { get; init; }  // From SimField.QueueRelativePosition
    public required Qty OrderQty { get; init; }
    public required Side OrderSide { get; init; }
    public required Price NominalFillPrice { get; init; }
    public required IHftDepth Depth { get; init; }
    public required Trade? Trade { get; init; }  // Null if not trade-driven fill
}
