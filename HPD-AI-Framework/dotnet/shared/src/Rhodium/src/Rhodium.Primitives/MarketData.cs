namespace Rhodium.Primitives;

/// <summary>
/// A two-sided quote (bid and ask) with dual timestamps for HFT.
/// </summary>
public readonly record struct Quote(
    Price Bid,
    Price Ask,
    Qty BidSize,
    Qty AskSize,
    DualTimestamp Time
)
{
    public Price Mid => new((Bid.Value + Ask.Value) / 2m, Bid.Currency);
    public Price Spread => Ask - Bid;
    public decimal SpreadBps => Bid.Value > 0 ? (Ask.Value - Bid.Value) / Mid.Value * 10000m : 0m;

    /// <summary>How stale is this quote?</summary>
    public Duration Staleness => Time.FeedLatency;

    // Tick-based access for HFT
    public TickPrice BidTick(decimal tickSize) => TickPrice.FromPrice(Bid, tickSize);
    public TickPrice AskTick(decimal tickSize) => TickPrice.FromPrice(Ask, tickSize);

    public static Quote Empty(DualTimestamp time) => new(Price.Zero, Price.Zero, Qty.Zero, Qty.Zero, time);
}

/// <summary>
/// A single trade that occurred with dual timestamps.
/// </summary>
public readonly record struct Trade(
    Price Price,
    Qty Size,
    Side AggressorSide,
    DualTimestamp Time
)
{
    public TickPrice PriceTick(decimal tickSize) => TickPrice.FromPrice(Price, tickSize);
}

/// <summary>
/// A depth update at a single price level.
/// </summary>
public readonly record struct DepthUpdate(
    Side Side,
    TickPrice PriceTick,
    Qty Size,
    DualTimestamp Time
);

/// <summary>
/// Order-book level update action.
/// </summary>
public enum BookAction : byte
{
    Add,
    Update,
    Delete,
    Clear
}

/// <summary>
/// A single price-level order-book delta.
/// </summary>
public readonly record struct BookDelta(
    Side Side,
    Price Price,
    Qty Size,
    BookAction Action,
    long Sequence = 0);

/// <summary>
/// A single price tick (for tick-by-tick data).
/// </summary>
public readonly record struct Tick(
    Price Price,
    Qty Size,
    TickType Type,
    DualTimestamp Time
)
{
    public TickPrice PriceTick(decimal tickSize) => TickPrice.FromPrice(Price, tickSize);
}

public enum TickType : byte { Trade, Bid, Ask, BidDepth, AskDepth }
