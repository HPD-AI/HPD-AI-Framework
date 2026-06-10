using Rhodium.Tensor;

namespace Rhodium.HFT;

/// <summary>
/// Market tensor fields for L3 order book tracking.
/// These live in a separate tensor store from strategy state.
/// Separate namespace from Field to avoid confusion with strategy fields.
/// </summary>
public static class MarketField
{
    // ==================== ORDER IDENTITY ====================

    /// <summary>
    /// Market order ID (0 = empty slot).
    /// Type: FactorF64 (untyped scalar - order IDs as double, safe for IDs less than 2^53).
    /// </summary>
    public static readonly VectorField<FactorF64> OrderId = new("market.order_id");

    /// <summary>
    /// Order quantity at this slot.
    /// Type: SizeF64 (quantity semantic).
    /// </summary>
    public static readonly VectorField<SizeF64> OrderQty = new("market.order_qty");

    /// <summary>
    /// Order timestamp (nanoseconds since epoch).
    /// Type: FactorF64 (untyped scalar - nanosecond count).
    /// </summary>
    public static readonly VectorField<FactorF64> OrderTimestamp = new("market.order_ts");

    // ==================== AGGREGATED VIEW (CACHE) ====================

    /// <summary>
    /// Total quantity at this price level (cached for performance).
    /// Updated when orders are added/removed.
    /// Type: SizeF64 (quantity semantic).
    /// </summary>
    public static readonly VectorField<SizeF64> TotalQtyAtLevel = new("market.total_qty");

    /// <summary>
    /// Number of active orders at this price level.
    /// Type: FactorF64 (untyped scalar - count).
    /// </summary>
    public static readonly VectorField<FactorF64> OrderCount = new("market.order_count");
}
