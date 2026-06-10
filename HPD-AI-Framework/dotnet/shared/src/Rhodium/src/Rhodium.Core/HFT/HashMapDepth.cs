using Rhodium.Primitives;

namespace Rhodium.HFT;

/// <summary>
/// HashMap-based depth - O(1) lookup and update.
/// Best for dense price ranges with predictable latency.
/// </summary>
public sealed class HashMapDepth : IHftDepth
{
    private readonly Dictionary<long, decimal> _bids = new();
    private readonly Dictionary<long, decimal> _asks = new();

    public decimal TickSize { get; }
    public decimal LotSize { get; }
    public long? BestBidTick { get; private set; }
    public long? BestAskTick { get; private set; }

    public HashMapDepth(decimal tickSize, decimal lotSize)
    {
        TickSize = tickSize;
        LotSize = lotSize;
    }

    public decimal QtyAtTick(Side side, long priceTick)
    {
        var book = side == Side.Buy ? _bids : _asks;
        return book.TryGetValue(priceTick, out var qty) ? qty : 0m;
    }

    public int CopyLevels(Side side, Span<DepthLevel> destination)
    {
        var count = 0;
        var book = side == Side.Buy ? _bids : _asks;

        foreach (var (priceTick, qty) in book)
        {
            if (side == Side.Buy)
                DepthLevelBuffer.InsertBid(destination, ref count, priceTick, qty);
            else
                DepthLevelBuffer.InsertAsk(destination, ref count, priceTick, qty);
        }

        return count;
    }

    public void Update(Side side, long priceTick, decimal qty, Instant timestamp)
    {
        var book = side == Side.Buy ? _bids : _asks;

        if (qty <= 0)
            book.Remove(priceTick);
        else
            book[priceTick] = qty;

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
