namespace Rhodium.Tensor;

/// <summary>
/// Three-dimensional tensor basis for L3 order book tracking.
/// Each VirtualIndex represents: (Instrument, PriceLevel, OrderSlot).
/// </summary>
public readonly struct MarketTensorBasis
{
    public int InstrumentDimension { get; }
    public int PriceLevelDimension { get; }
    public int OrderSlotDimension { get; }
    public int Rank => InstrumentDimension * PriceLevelDimension * OrderSlotDimension;

    public MarketTensorBasis(int instrumentDim, int priceLevelDim, int orderSlotDim)
    {
        InstrumentDimension = instrumentDim;
        PriceLevelDimension = priceLevelDim;
        OrderSlotDimension = orderSlotDim;
    }

    /// <summary>
    /// Convert (instrument_idx, price_level_idx, order_slot_idx) to linear VirtualIndex.
    /// </summary>
    public int ToLinear(int instrumentIdx, int priceLevelIdx, int slotIdx) =>
        (instrumentIdx * PriceLevelDimension + priceLevelIdx) * OrderSlotDimension + slotIdx;

    /// <summary>
    /// Get VirtualIndex range for all order slots at a specific (instrument, price level).
    /// </summary>
    public (int Start, int Length) GetPriceLevelRange(int instrumentIdx, int priceLevelIdx)
    {
        int start = ToLinear(instrumentIdx, priceLevelIdx, 0);
        return (start, OrderSlotDimension);
    }
}
