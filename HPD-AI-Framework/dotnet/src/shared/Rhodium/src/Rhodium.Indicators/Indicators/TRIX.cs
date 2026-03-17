namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming TRIX (Triple Exponential Moving Average ROC).
/// O(1) update, zero allocations.
/// Measures rate of change of a triple-smoothed EMA.
/// </summary>
public sealed class TRIX : PriceIndicatorBase
{
    private readonly EMA _ema1;
    private readonly EMA _ema2;
    private readonly EMA _ema3;
    private decimal _prevEma3;

    public override bool IsReady => _ema3.IsReady && _count > 1;

    public TRIX(int period)
    {
        _ema1 = new EMA(period);
        _ema2 = new EMA(period);
        _ema3 = new EMA(period);
    }

    public override void Update(decimal price)
    {
        _count++;

        // Chain the EMAs
        _ema1.Update(price);
        _ema2.Update(_ema1.Value);

        _prevEma3 = _ema3.Value;
        _ema3.Update(_ema2.Value);

        // Calculate 1-period ROC of triple EMA
        if (_prevEma3 != 0 && IsReady)
        {
            _value = (_ema3.Value - _prevEma3) / _prevEma3 * 100m;
        }
        else
        {
            _value = 0m;
        }
    }

    public override void Reset()
    {
        base.Reset();
        _ema1.Reset();
        _ema2.Reset();
        _ema3.Reset();
        _prevEma3 = 0m;
    }
}
