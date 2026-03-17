namespace Rhodium.HFT;

/// <summary>
/// Configuration for L3 Market-By-Order tensor space.
/// Defines the capacity and layout of the market tensor space for order book tracking.
/// </summary>
public sealed record MarketTensorSpaceConfig
{
    /// <summary>
    /// Number of instruments to track.
    /// Default: 500 (matches typical strategy universe).
    /// </summary>
    public int InstrumentCount { get; init; } = 500;

    /// <summary>
    /// Price levels per instrument to track.
    /// Typically 100 levels above and 100 levels below mid.
    /// Default: 200.
    /// </summary>
    public int PriceLevelsPerInstrument { get; init; } = 200;

    /// <summary>
    /// Maximum order slots per price level.
    /// Determines FIFO queue depth for L3 tracking.
    /// Default: 100 (sufficient for most markets).
    /// </summary>
    public int OrderSlotsPerLevel { get; init; } = 100;

    /// <summary>
    /// Total virtual indices in market space.
    /// = InstrumentCount × PriceLevelsPerInstrument × OrderSlotsPerLevel
    /// Example: 500 × 200 × 100 = 10M VIs
    /// </summary>
    public int TotalMarketVIs => InstrumentCount * PriceLevelsPerInstrument * OrderSlotsPerLevel;

    /// <summary>
    /// Sparse allocation: Only allocate pages for price levels with active orders.
    /// Memory savings: ~95% (most price levels empty most of the time).
    /// </summary>
    public bool SparseAllocation { get; init; } = true;
}
