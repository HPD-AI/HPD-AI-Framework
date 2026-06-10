namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Triple Exponential Moving Average.
/// O(1) update, zero allocations.
/// Formula: 3*EMA1 - 3*EMA2 + EMA3
/// </summary>
public sealed class TEMA : PriceIndicatorBase
{
    private readonly EMA _ema1;
    private readonly EMA _ema2;
    private readonly EMA _ema3;

    public override bool IsReady => _ema3.IsReady;

    public TEMA(int period)
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
        _ema3.Update(_ema2.Value);

        // TEMA formula: 3*EMA1 - 3*EMA2 + EMA3
        _value = 3m * _ema1.Value - 3m * _ema2.Value + _ema3.Value;
    }

    public override void Reset()
    {
        base.Reset();
        _ema1.Reset();
        _ema2.Reset();
        _ema3.Reset();
    }
}
