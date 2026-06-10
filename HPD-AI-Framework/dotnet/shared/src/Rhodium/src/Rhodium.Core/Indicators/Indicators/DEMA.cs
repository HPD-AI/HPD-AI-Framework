namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Double Exponential Moving Average.
/// O(1) update, zero allocations.
/// DEMA = 2 * EMA(period) - EMA(EMA(period))
/// </summary>
public sealed class DEMA : PriceIndicatorBase
{
    private readonly EMA _ema1;
    private readonly EMA _ema2;

    public override bool IsReady => _ema1.IsReady && _ema2.IsReady;

    public DEMA(int period)
    {
        _ema1 = new EMA(period);
        _ema2 = new EMA(period);
    }

    public override void Update(decimal price)
    {
        _count++;
        _ema1.Update(price);
        _ema2.Update(_ema1.Value);

        _value = 2m * _ema1.Value - _ema2.Value;
    }

    public override void Reset()
    {
        base.Reset();
        _ema1.Reset();
        _ema2.Reset();
    }
}
