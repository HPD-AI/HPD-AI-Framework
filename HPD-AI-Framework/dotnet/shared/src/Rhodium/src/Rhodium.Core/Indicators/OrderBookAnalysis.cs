namespace Rhodium.Indicators;

/// <summary>
/// Static helper functions for order book analysis.
/// Pure functions with zero allocations.
/// </summary>
public static class OrderBookAnalysis
{
    /// <summary>
    /// Calculate Book Imbalance Ratio at best bid/ask.
    /// Positive = more bid size (buying pressure), Negative = more ask size (selling pressure).
    /// Range: -1 to +1.
    /// </summary>
    public static decimal BookImbalanceRatio(decimal bestBidSize, decimal bestAskSize)
    {
        var total = bestBidSize + bestAskSize;
        return total > 0 ? (bestBidSize - bestAskSize) / total : 0;
    }

    /// <summary>
    /// Calculate Weighted Book Imbalance across multiple depth levels.
    /// Higher weight given to better prices (closer to mid).
    /// Range: -1 to +1.
    /// </summary>
    public static decimal WeightedBookImbalance(
        ReadOnlySpan<(decimal Price, decimal Size)> bids,
        ReadOnlySpan<(decimal Price, decimal Size)> asks,
        int levels = 5)
    {
        var bidWeight = 0m;
        var askWeight = 0m;

        var levelsToUse = Math.Min(levels, Math.Min(bids.Length, asks.Length));

        for (int i = 0; i < levelsToUse; i++)
        {
            var weight = levelsToUse - i; // Higher weight for better prices
            bidWeight += bids[i].Size * weight;
            askWeight += asks[i].Size * weight;
        }

        var total = bidWeight + askWeight;
        return total > 0 ? (bidWeight - askWeight) / total : 0;
    }

    /// <summary>
    /// Analyze bid-ask spread metrics.
    /// </summary>
    public static (decimal Spread, decimal SpreadBps, decimal MidPrice) AnalyzeSpread(
        decimal bidPrice,
        decimal askPrice)
    {
        var spread = askPrice - bidPrice;
        var midPrice = (bidPrice + askPrice) / 2;
        var spreadBps = midPrice > 0 ? spread / midPrice * 10000 : 0; // Basis points
        return (spread, spreadBps, midPrice);
    }

    /// <summary>
    /// Calculate total depth (liquidity) on bid and ask sides.
    /// </summary>
    public static (decimal BidDepth, decimal AskDepth, decimal TotalDepth) CalculateDepth(
        ReadOnlySpan<(decimal Price, decimal Size)> bids,
        ReadOnlySpan<(decimal Price, decimal Size)> asks,
        int levels = 10)
    {
        var bidDepth = 0m;
        var askDepth = 0m;

        var bidLevels = Math.Min(levels, bids.Length);
        var askLevels = Math.Min(levels, asks.Length);

        for (int i = 0; i < bidLevels; i++)
            bidDepth += bids[i].Size;

        for (int i = 0; i < askLevels; i++)
            askDepth += asks[i].Size;

        return (bidDepth, askDepth, bidDepth + askDepth);
    }

    /// <summary>
    /// Calculate price impact for a given order size.
    /// Returns the average price at which the order would be filled.
    /// </summary>
    public static decimal CalculatePriceImpact(
        ReadOnlySpan<(decimal Price, decimal Size)> levels,
        decimal orderSize,
        bool isBuy)
    {
        if (orderSize <= 0 || levels.Length == 0)
            return 0;

        var remaining = orderSize;
        var totalCost = 0m;
        var filledSize = 0m;

        for (int i = 0; i < levels.Length && remaining > 0; i++)
        {
            var levelSize = levels[i].Size;
            var fillSize = Math.Min(remaining, levelSize);

            totalCost += fillSize * levels[i].Price;
            filledSize += fillSize;
            remaining -= fillSize;
        }

        return filledSize > 0 ? totalCost / filledSize : 0;
    }
}
