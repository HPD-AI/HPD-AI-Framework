using Rhodium.Primitives;

namespace Rhodium.HFT;

/// <summary>
/// Algebraic view of order book depth.
/// Provides best bid/ask and crossing detection.
/// </summary>
public readonly struct OrderLattice
{
    public TickPrice BestBid { get; }
    public TickPrice BestAsk { get; }

    /// <summary>
    /// True if bid >= ask, indicating a crossed market.
    /// </summary>
    public bool IsCrossed => BestBid >= BestAsk;

    public OrderLattice(TickPrice bid, TickPrice ask)
    {
        BestBid = bid;
        BestAsk = ask;
    }

    /// <summary>
    /// Creates an OrderLattice from an IHftDepth implementation.
    /// </summary>
    public static OrderLattice FromDepth(IHftDepth depth) =>
        new(
            depth.BestBidTick.HasValue
                ? new TickPrice(depth.BestBidTick.Value, depth.TickSize)
                : new TickPrice(0, depth.TickSize),
            depth.BestAskTick.HasValue
                ? new TickPrice(depth.BestAskTick.Value, depth.TickSize)
                : new TickPrice(0, depth.TickSize)
        );
}
