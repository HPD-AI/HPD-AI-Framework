using Rhodium.Primitives;

namespace Rhodium.HFT;

/// <summary>
/// Fused depth - handles out-of-order feeds by storing timestamp per level.
/// Rejects stale updates automatically.
/// </summary>
public sealed class FusedDepth : IHftDepth
{
    private readonly Dictionary<long, (decimal Qty, long Timestamp)> _bids = new();
    private readonly Dictionary<long, (decimal Qty, long Timestamp)> _asks = new();

    public decimal TickSize { get; }
    public decimal LotSize { get; }
    public long? BestBidTick { get; private set; }
    public long? BestAskTick { get; private set; }

    public FusedDepth(decimal tickSize, decimal lotSize)
    {
        TickSize = tickSize;
        LotSize = lotSize;
    }

    public decimal QtyAtTick(Side side, long priceTick)
    {
        var book = side == Side.Buy ? _bids : _asks;
        return book.TryGetValue(priceTick, out var entry) ? entry.Qty : 0m;
    }

    public int CopyLevels(Side side, Span<DepthLevel> destination)
    {
        var count = 0;
        var book = side == Side.Buy ? _bids : _asks;

        foreach (var (priceTick, entry) in book)
        {
            if (side == Side.Buy)
                DepthLevelBuffer.InsertBid(destination, ref count, priceTick, entry.Qty);
            else
                DepthLevelBuffer.InsertAsk(destination, ref count, priceTick, entry.Qty);
        }

        return count;
    }

    public void Update(Side side, long priceTick, decimal qty, Instant timestamp)
    {
        var book = side == Side.Buy ? _bids : _asks;
        var ts = timestamp.Nanos;

        // Reject stale updates
        if (book.TryGetValue(priceTick, out var existing) && existing.Timestamp > ts)
            return;

        if (qty <= 0)
            book.Remove(priceTick);
        else
            book[priceTick] = (qty, ts);

        // Update best bid/ask
        if (side == Side.Buy)
            BestBidTick = book.Count > 0 ? book.Keys.Max() : null;
        else
            BestAskTick = book.Count > 0 ? book.Keys.Min() : null;
    }

    public void Clear(Side side = Side.None)
    {
        if (side == Side.None || side == Side.Buy)
        {
            _bids.Clear();
            BestBidTick = null;
        }
        if (side == Side.None || side == Side.Sell)
        {
            _asks.Clear();
            BestAskTick = null;
        }
    }
}
