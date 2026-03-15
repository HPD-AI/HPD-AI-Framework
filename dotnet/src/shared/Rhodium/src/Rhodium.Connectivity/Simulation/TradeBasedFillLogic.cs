using Rhodium.Primitives;

namespace Rhodium.Connectivity.Simulation;

/// <summary>
/// Trade-based fill logic for aggressor-side modeling.
/// Provides more realistic fills for passive orders when trade ticks are available.
/// </summary>
public static class TradeBasedFillLogic
{
    /// <summary>
    /// Determines if trade tick provides fill opportunity for resting order.
    /// Key Insight: A trade at price P indicates that liquidity was accessed at P by the aggressor:
    /// - SELLER trade (aggressor sold) → passive BUY orders at P can fill
    /// - BUYER trade (aggressor bought) → passive SELL orders at P can fill
    /// </summary>
    public static bool CanFillFromTrade(
        long orderPriceTick,
        Side orderSide,
        Trade trade,
        decimal tickSize,
        long? currentBestBid,
        long? currentBestAsk)
    {
        long tradeTick = trade.PriceTick(tickSize).Ticks;

        if (orderSide == Side.Buy)
        {
            // BUY order can fill from SELLER trade (aggressor sold into our bid)
            if (trade.AggressorSide != Side.Sell)
                return false;

            // Our buy order must be at or above the trade price
            if (orderPriceTick < tradeTick)
                return false;

            // Trade provides evidence of fillable liquidity at tradeTick
            // Temporarily treat tradeTick as effective ask
            long effectiveAsk = currentBestAsk.HasValue
                ? Math.Min(currentBestAsk.Value, tradeTick)
                : tradeTick;

            return orderPriceTick >= effectiveAsk;
        }
        else // Sell
        {
            // SELL order can fill from BUYER trade (aggressor bought from our ask)
            if (trade.AggressorSide != Side.Buy)
                return false;

            // Our sell order must be at or below the trade price
            if (orderPriceTick > tradeTick)
                return false;

            // Temporarily treat tradeTick as effective bid
            long effectiveBid = currentBestBid.HasValue
                ? Math.Max(currentBestBid.Value, tradeTick)
                : tradeTick;

            return orderPriceTick <= effectiveBid;
        }
    }

    /// <summary>
    /// Determines fill price for trade-driven fill.
    /// Conservative approach: fill at order's limit price (not trade price).
    /// Filling at the trade price would give unrealistic price improvement.
    /// </summary>
    public static Price GetTradeDrivenFillPrice(long orderPriceTick, decimal tickSize, Currency currency)
    {
        decimal fillPrice = orderPriceTick * tickSize;
        return new Price(fillPrice, currency);
    }

    /// <summary>
    /// Determine fill quantity based on trade size and fill behavior.
    /// </summary>
    public static Qty DetermineFillQuantity(Qty orderRemainingQty, Trade? trade, FillBehavior behavior)
    {
        if (behavior == FillBehavior.NoPartialFill)
            return orderRemainingQty;

        if (trade.HasValue)
        {
            return orderRemainingQty.Value < trade.Value.Size.Value
                ? orderRemainingQty
                : trade.Value.Size;
        }

        return orderRemainingQty;
    }
}
