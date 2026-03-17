using Rhodium.Primitives;

namespace Rhodium.HFT;

/// <summary>
/// HFT market depth interface with tick-based operations.
/// All implementations provide O(1) or O(log n) operations for high-frequency trading.
/// </summary>
public interface IHftDepth
{
    /// <summary>Minimum price increment.</summary>
    decimal TickSize { get; }

    /// <summary>Minimum order size increment.</summary>
    decimal LotSize { get; }

    /// <summary>Best bid price in ticks (null if no liquidity).</summary>
    long? BestBidTick { get; }

    /// <summary>Best ask price in ticks (null if no liquidity).</summary>
    long? BestAskTick { get; }

    /// <summary>Get quantity at a specific price tick.</summary>
    decimal QtyAtTick(Side side, long priceTick);

    /// <summary>Update depth from market event.</summary>
    void Update(Side side, long priceTick, decimal qty, Instant timestamp);

    /// <summary>Clear one or both sides of the book.</summary>
    void Clear(Side side = Side.None);
}
