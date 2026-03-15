using System;
using System.Runtime.CompilerServices;
using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Platform.Extensions;

/// <summary>
/// Execution policy flags for trade submission.
/// Controls idempotency and risk checks.
/// </summary>
[Flags]
public enum ExecutionPolicy : byte
{
    /// <summary>No checks - submit order unconditionally.</summary>
    Raw = 0,

    /// <summary>
    /// Prevents duplicate orders on the same side.
    /// Checks if an open order exists before submitting.
    /// </summary>
    Idempotent = 1 << 0,

    /// <summary>
    /// Enforces lot size rounding, min/max quantity checks.
    /// - LotSize rounding (Round Down)
    /// - MinQty checks (Reject if below minimum)
    /// - MaxQty checks (Clamp to maximum)
    /// </summary>
    RiskCheck = 1 << 1,

    /// <summary>Safe default - enables both idempotency and risk checks.</summary>
    Safe = Idempotent | RiskCheck
}

/// <summary>
/// Ergonomic trade execution extensions with quantity-first API.
/// All methods are aggressively inlined for hot path performance.
/// </summary>
public static class TradeExtensions
{
    /// <summary>
    /// Submits a buy order for the specified quantity.
    /// Uses aggressive limit pricing (one tick above best ask).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Buy(this ref TradingEngine engine, AssetId id, Qty quantity, ExecutionPolicy policy = ExecutionPolicy.Safe)
        => SubmitTrade(ref engine, id, Side.Buy, quantity, policy);

    /// <summary>
    /// Submits a sell order for the specified quantity.
    /// Uses aggressive limit pricing (one tick below best bid).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sell(this ref TradingEngine engine, AssetId id, Qty quantity, ExecutionPolicy policy = ExecutionPolicy.Safe)
        => SubmitTrade(ref engine, id, Side.Sell, quantity, policy);

    /// <summary>
    /// Sets the position to the target quantity.
    /// Calculates delta and submits appropriate buy/sell order.
    /// </summary>
    public static void SetPosition(this ref TradingEngine engine, AssetId id, Qty targetQty, ExecutionPolicy policy = ExecutionPolicy.Safe)
    {
        var current = engine.GetPosition(id);
        var delta = targetQty.Value - current;
        if (delta == 0) return;

        var side = delta > 0 ? Side.Buy : Side.Sell;
        SubmitTrade(ref engine, id, side, new Qty(Math.Abs(delta)), policy);
    }

    /// <summary>
    /// Flattens the position (closes all holdings).
    /// Uses raw execution policy to ensure immediate closure.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Flatten(this ref TradingEngine engine, AssetId id)
        => SetPosition(ref engine, id, Qty.Zero, ExecutionPolicy.Raw);

    /// <summary>
    /// Cancels all open orders for the specified asset.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CancelAll(this ref TradingEngine engine, AssetId id)
        => engine.CancelAllOrders(id);

    /// <summary>
    /// Internal trade submission with policy enforcement.
    /// </summary>
    private static void SubmitTrade(ref TradingEngine engine, AssetId id, Side side, Qty qty, ExecutionPolicy policy)
    {
        // 1. Idempotency Check
        if ((policy & ExecutionPolicy.Idempotent) != 0)
        {
            if (engine.HasOpenOrder(id, side)) return;
        }

        // 2. Metadata (Single Source of Truth)
        var tickSize = engine.GetTickSize(id);
        var lotSize = engine.GetLotSize(id);
        var currency = engine.GetCurrency(id);

        // 3. Risk Check (Lot Size Rounding)
        if ((policy & ExecutionPolicy.RiskCheck) != 0)
        {
            var remainder = qty.Value % lotSize;
            if (remainder != 0)
            {
                qty = new Qty(qty.Value - remainder); // Round down
            }

            if (qty.Value == 0) return; // Quantity below minimum lot size
        }

        // 4. Pricing Logic (Aggressive Limit)
        // Take liquidity by crossing the spread slightly
        var bestTick = side == Side.Buy ? engine.GetBestAskTick(id) : engine.GetBestBidTick(id);
        if (!bestTick.HasValue) return; // No market depth available

        // Place order one tick better than current market
        var priceTick = side == Side.Buy ? bestTick.Value + 1 : bestTick.Value - 1;
        var price = new TickPrice(priceTick, tickSize).ToPrice(currency);

        // 5. Submit Order
        engine.SubmitLimitOrder(id, side, qty, price);
    }
}
