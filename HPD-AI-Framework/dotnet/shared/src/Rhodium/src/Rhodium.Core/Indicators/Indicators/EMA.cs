namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Exponential Moving Average.
/// O(1) update, zero allocations.
/// </summary>
public sealed class EMA : PriceIndicatorBase
{
    private readonly int _period;
    private readonly decimal _multiplier;

    public override bool IsReady => _count >= _period;

    public EMA(int period)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _multiplier = 2m / (period + 1);
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
            // EMA formula: EMA = (Price - PrevEMA) * Multiplier + PrevEMA
            _value = (price - _value) * _multiplier + _value;
        }
    }
}
