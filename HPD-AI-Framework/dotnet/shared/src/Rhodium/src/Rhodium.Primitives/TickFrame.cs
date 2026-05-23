namespace Rhodium.Primitives;

/// <summary>
/// Allocation-free tick/depth frame consumed by generated tick indicators.
/// </summary>
public readonly record struct TickFrame(
    AssetId AssetId,
    TickEventType EventType,
    long? BidTick,
    long? AskTick,
    decimal BidSize,
    decimal AskSize,
    decimal TickSize,
    Instant ExchangeTime)
{
    public bool HasQuote => BidTick.HasValue && AskTick.HasValue;
    public long SpreadTicks => HasQuote ? AskTick!.Value - BidTick!.Value : 0;
    public decimal BidPrice => BidTick.HasValue ? BidTick.Value * TickSize : 0m;
    public decimal AskPrice => AskTick.HasValue ? AskTick.Value * TickSize : 0m;
    public decimal MidPrice => HasQuote ? (BidPrice + AskPrice) / 2m : 0m;
    public decimal MicroPrice
    {
        get
        {
            if (!HasQuote) return 0m;
            var totalSize = BidSize + AskSize;
            return totalSize > 0m
                ? (AskPrice * BidSize + BidPrice * AskSize) / totalSize
                : MidPrice;
        }
    }
}

public enum TickEventType : byte
{
    Snapshot,
    Quote,
    Trade,
    Depth
}
