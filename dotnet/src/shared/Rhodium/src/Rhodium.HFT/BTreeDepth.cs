using Rhodium.Primitives;

namespace Rhodium.HFT;

/// <summary>
/// BTree-based depth - O(log n) lookup and update.
/// Best for sparse price ranges where most ticks have no liquidity.
/// Uses SortedDictionary for efficient sorted operations.
/// </summary>
public sealed class BTreeDepth : IHftDepth
{
    private readonly SortedDictionary<long, decimal> _bids = new();
    private readonly SortedDictionary<long, decimal> _asks = new();

    public decimal TickSize { get; }
    public decimal LotSize { get; }
    public long? BestBidTick { get; private set; }
    public long? BestAskTick { get; private set; }

    public BTreeDepth(decimal tickSize, decimal lotSize)
    {
        TickSize = tickSize;
        LotSize = lotSize;
    }

    public decimal QtyAtTick(Side side, long priceTick)
    {
        var book = side == Side.Buy ? _bids : _asks;
        return book.TryGetValue(priceTick, out var qty) ? qty : 0m;
    }

    public void Update(Side side, long priceTick, decimal qty, Instant timestamp)
    {
        var book = side == Side.Buy ? _bids : _asks;

        if (qty <= 0)
            book.Remove(priceTick);
        else
            book[priceTick] = qty;

        // Update best bid/ask (efficient with SortedDictionary)
        if (side == Side.Buy)
            BestBidTick = book.Count > 0 ? book.Keys.Last() : null;
        else
            BestAskTick = book.Count > 0 ? book.Keys.First() : null;
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
