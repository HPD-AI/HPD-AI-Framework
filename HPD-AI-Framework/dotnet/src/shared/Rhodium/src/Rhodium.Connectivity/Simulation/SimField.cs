using Rhodium.Tensor;

namespace Rhodium.Connectivity.Simulation;

/// <summary>
/// Simulation-specific tensor fields.
/// Only allocated when SimulationConfig is active (not present in live trading).
///
/// Type Policy: Simulation reuses FactorF64 for untyped scalars (ticks, nanos, flags)
/// where semantic enforcement is less critical than kernel compatibility.
/// This is an explicit carve-out from the "semantic wrappers enforce correctness" rule.
/// </summary>
public static class SimField
{
    // ==================== QUEUE STATE ====================

    /// <summary>
    /// Relative queue position at current limit price (0.0 = front, 1.0 = tail).
    /// Updated when depth changes or trades occur at the price level.
    /// Type: FactorF64 (dimensionless ratio - correct semantic).
    /// </summary>
    public static readonly VectorField<FactorF64> QueueRelativePosition = new("sim.queue_pos");

    /// <summary>
    /// Quantity ahead in queue (lot-size units).
    /// Type: SizeF64 (quantity semantic - correct).
    /// </summary>
    public static readonly VectorField<SizeF64> QueueAheadQty = new("sim.queue_ahead");

    // ==================== LATENCY ====================

    /// <summary>
    /// Pre-sampled entry latency in nanoseconds (local → exchange).
    /// Sampled once during initialization using deterministic PRNG.
    /// Type: FactorF64 (untyped scalar carve-out - nanosecond count).
    /// </summary>
    public static readonly VectorField<FactorF64> EntryLatencyNanos = new("sim.entry_latency_ns");

    /// <summary>
    /// Pre-sampled response latency in nanoseconds (exchange → local).
    /// Type: FactorF64 (untyped scalar carve-out).
    /// </summary>
    public static readonly VectorField<FactorF64> ResponseLatencyNanos = new("sim.response_latency_ns");

    // ==================== FILL TRACKING ====================

    /// <summary>
    /// Last fill was maker (1.0) or taker (0.0).
    /// Used for fee calculation.
    /// Type: FactorF64 (flag value - untyped scalar carve-out).
    /// </summary>
    public static readonly VectorField<FactorF64> LastFillWasMaker = new("sim.last_fill_maker");

    // ==================== BUY ORDER STATE ====================

    /// <summary>
    /// Active buy order limit price in ticks (0 = no active order).
    /// Type: FactorF64 (untyped scalar carve-out - tick count).
    /// </summary>
    public static readonly VectorField<FactorF64> BuyOrderPriceTick = new("sim.buy_order_tick");

    /// <summary>
    /// Active buy order quantity.
    /// Type: SizeF64 (quantity semantic - correct).
    /// </summary>
    public static readonly VectorField<SizeF64> BuyOrderQty = new("sim.buy_order_qty");

    /// <summary>
    /// Active buy order ID (0 = no active order).
    /// Type: FactorF64 (untyped scalar carve-out - order ID as double).
    /// Note: OrderId is a long, stored as double (safe for IDs < 2^53).
    /// </summary>
    public static readonly VectorField<FactorF64> BuyOrderId = new("sim.buy_order_id");

    // ==================== SELL ORDER STATE ====================

    /// <summary>
    /// Active sell order limit price in ticks (0 = no active order).
    /// Type: FactorF64 (untyped scalar carve-out).
    /// </summary>
    public static readonly VectorField<FactorF64> SellOrderPriceTick = new("sim.sell_order_tick");

    /// <summary>
    /// Active sell order quantity.
    /// Type: SizeF64 (quantity semantic - correct).
    /// </summary>
    public static readonly VectorField<SizeF64> SellOrderQty = new("sim.sell_order_qty");

    /// <summary>
    /// Active sell order ID (0 = no active order).
    /// Type: FactorF64 (untyped scalar carve-out).
    /// </summary>
    public static readonly VectorField<FactorF64> SellOrderId = new("sim.sell_order_id");
}
