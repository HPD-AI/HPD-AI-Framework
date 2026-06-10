namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Bias - percentage deviation from moving average.
/// O(1) update, zero allocations.
/// </summary>
public sealed class Bias : PriceIndicatorBase
{
    private readonly SMA _sma;
    private decimal _currentPrice;

    public override bool IsReady => _sma.IsReady;

    public Bias(int period)
    {
        _sma = new SMA(period);
    }

    public override void Update(decimal price)
    {
        _count++;
        _currentPrice = price;
        _sma.Update(price);

        var ma = _sma.Value;
        _value = ma != 0 ? (price - ma) / ma * 100m : 0m;
    }

    public override void Reset()
    {
        base.Reset();
        _sma.Reset();
        _currentPrice = 0m;
    }
}
