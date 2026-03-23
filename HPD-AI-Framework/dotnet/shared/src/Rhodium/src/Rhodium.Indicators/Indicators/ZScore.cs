namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Z-Score - standardized score (distance from mean in standard deviations).
/// O(1) update, uses SMA and StdDev internally.
/// </summary>
public sealed class ZScore : PriceIndicatorBase
{
    private readonly int _period;
    private readonly SMA _sma;
    private readonly StdDev _stdDev;
    private decimal _lastPrice;

    public override bool IsReady => _sma.IsReady && _stdDev.IsReady;

    public ZScore(int period)
    {
        if (period < 2)
            throw new ArgumentException("Period must be >= 2", nameof(period));

        _period = period;
        _sma = new SMA(period);
        _stdDev = new StdDev(period);
    }

    public override void Update(decimal price)
    {
        _lastPrice = price;
        _sma.Update(price);
        _stdDev.Update(price);
        _count++;

        if (IsReady)
        {
            var std = _stdDev.Value;
            _value = std > 0 ? (price - _sma.Value) / std : 0;
        }
    }

    public override void Reset()
    {
        base.Reset();
        _sma.Reset();
        _stdDev.Reset();
        _lastPrice = 0m;
    }
}
