namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Wilder's Moving Average (RMA).
/// O(1) update, zero allocations.
/// Used in RSI, ATR, and ADX calculations.
/// </summary>
public sealed class RMA : PriceIndicatorBase
{
    private readonly int _period;
    private readonly decimal _alpha;

    public override bool IsReady => _count >= _period;

    public RMA(int period)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _alpha = 1m / period;
    }

    public override void Update(decimal price)
    {
        _count++;

        if (_count == 1)
        {
            _value = price;
        }
        else
        {
            // RMA formula: RMA = Alpha * Price + (1 - Alpha) * PrevRMA
            _value = _alpha * price + (1 - _alpha) * _value;
        }
    }
}
