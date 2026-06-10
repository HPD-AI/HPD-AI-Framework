using Rhodium.Primitives;

namespace Rhodium.HFT;

/// <summary>
/// ROI Vector depth - O(1) lookup with cache-friendly contiguous memory layout.
/// Best for market-making strategies focused around mid-price.
/// Uses a fixed-size array covering a price range (Region of Interest).
/// </summary>
public sealed class RoiVectorDepth : IHftDepth
{
    private readonly decimal[] _bids;
    private readonly decimal[] _asks;
    private readonly long _lowerBound;

    public decimal TickSize { get; }
    public decimal LotSize { get; }
    public long? BestBidTick { get; private set; }
    public long? BestAskTick { get; private set; }

    public RoiVectorDepth(decimal tickSize, decimal lotSize, long lowerBound, int rangeSize)
    {
        TickSize = tickSize;
        LotSize = lotSize;
        _lowerBound = lowerBound;
        _bids = new decimal[rangeSize];
        _asks = new decimal[rangeSize];
    }

    private int TickToIndex(long tick) => (int)(tick - _lowerBound);
    private bool InRange(long tick) => tick >= _lowerBound && tick < _lowerBound + _bids.Length;

    public decimal QtyAtTick(Side side, long priceTick)
    {
        if (!InRange(priceTick)) return 0m;
        var arr = side == Side.Buy ? _bids : _asks;
        return arr[TickToIndex(priceTick)];
    }

    public int CopyLevels(Side side, Span<DepthLevel> destination)
    {
        if (destination.IsEmpty)
            return 0;

        var count = 0;
        if (side == Side.Buy)
        {
            for (var i = _bids.Length - 1; i >= 0 && count < destination.Length; i--)
            {
                var qty = _bids[i];
                if (qty > 0m)
                    destination[count++] = new DepthLevel(_lowerBound + i, qty);
            }
        }
        else
        {
            for (var i = 0; i < _asks.Length && count < destination.Length; i++)
            {
                var qty = _asks[i];
                if (qty > 0m)
                    destination[count++] = new DepthLevel(_lowerBound + i, qty);
            }
        }

        return count;
    }

    public void Update(Side side, long priceTick, decimal qty, Instant timestamp)
    {
        if (!InRange(priceTick)) return;

        var arr = side == Side.Buy ? _bids : _asks;
        arr[TickToIndex(priceTick)] = qty;

        // Update best (scan is cache-friendly due to contiguous array)
        if (side == Side.Buy)
        {
            BestBidTick = null;
            for (int i = _bids.Length - 1; i >= 0; i--)
            {
                if (_bids[i] > 0)
                {
                    BestBidTick = _lowerBound + i;
                    break;
                }
            }
        }
        else
        {
            BestAskTick = null;
            for (int i = 0; i < _asks.Length; i++)
            {
                if (_asks[i] > 0)
                {
                    BestAskTick = _lowerBound + i;
                    break;
                }
            }
        }
    }

    public void Clear(Side side = Side.None)
    {
        if (side == Side.None || side == Side.Buy)
        {
            Array.Clear(_bids);
            BestBidTick = null;
        }
        if (side == Side.None || side == Side.Sell)
        {
            Array.Clear(_asks);
            BestAskTick = null;
        }
    }
}
