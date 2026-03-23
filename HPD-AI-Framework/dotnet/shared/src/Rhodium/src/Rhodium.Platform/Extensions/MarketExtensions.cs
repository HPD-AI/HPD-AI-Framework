using System.Runtime.CompilerServices;
using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Platform.Extensions;

/// <summary>
/// Flattened L1/L2 market data accessors for HFT strategies.
/// Zero-dispatch hot path guaranteed for all methods.
/// </summary>
public static class MarketExtensions
{
    /// <summary>
    /// Tries to get the best bid price in ticks.
    /// Returns true if a bid exists, false otherwise.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetBid(this ref TradingEngine engine, AssetId id, out long ticks)
    {
        var t = engine.GetBestBidTick(id);
        ticks = t.GetValueOrDefault();
        return t.HasValue;
    }

    /// <summary>
    /// Tries to get the best ask price in ticks.
    /// Returns true if an ask exists, false otherwise.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetAsk(this ref TradingEngine engine, AssetId id, out long ticks)
    {
        var t = engine.GetBestAskTick(id);
        ticks = t.GetValueOrDefault();
        return t.HasValue;
    }

    /// <summary>
    /// Gets the total quantity at the best bid level.
    /// Returns 0 if no bid exists.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal GetBidDepth(this ref TradingEngine engine, AssetId id)
    {
        var best = engine.GetBestBidTick(id);
        if (!best.HasValue) return 0m;
        return engine.GetQtyAtTick(id, Side.Buy, best.Value);
    }

    /// <summary>
    /// Gets the total quantity at the best ask level.
    /// Returns 0 if no ask exists.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal GetAskDepth(this ref TradingEngine engine, AssetId id)
    {
        var best = engine.GetBestAskTick(id);
        if (!best.HasValue) return 0m;
        return engine.GetQtyAtTick(id, Side.Sell, best.Value);
    }

    /// <summary>
    /// Gets the bid-ask spread in ticks.
    /// Returns 0 if no valid spread exists.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long GetSpreadTicks(this ref TradingEngine engine, AssetId id)
    {
        var bid = engine.GetBestBidTick(id);
        var ask = engine.GetBestAskTick(id);
        if (!bid.HasValue || !ask.HasValue) return 0;
        return ask.Value - bid.Value;
    }

    /// <summary>
    /// Gets the mid price in ticks (average of best bid and ask).
    /// Returns null if no valid mid price can be calculated.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long? GetMidTick(this ref TradingEngine engine, AssetId id)
    {
        var bid = engine.GetBestBidTick(id);
        var ask = engine.GetBestAskTick(id);
        if (!bid.HasValue || !ask.HasValue) return null;
        return (bid.Value + ask.Value) / 2;
    }
}
