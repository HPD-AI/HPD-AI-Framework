namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Hull Moving Average.
/// O(1) update, zero allocations.
/// HMA = WMA(2*WMA(n/2) - WMA(n), sqrt(n))
/// Full implementation with final WMA smoothing.
/// </summary>
public sealed class HMA : PriceIndicatorBase
{
    private readonly int _sqrtPeriod;
    private readonly WMA _wmaHalf;
    private readonly WMA _wmaFull;
    private readonly WMA _wmaFinal;

    public override bool IsReady => _wmaHalf.IsReady && _wmaFull.IsReady && _wmaFinal.IsReady;

    public HMA(int period)
    {
        var halfPeriod = period / 2;
        _sqrtPeriod = (int)Math.Sqrt(period);

        _wmaHalf = new WMA(halfPeriod);
        _wmaFull = new WMA(period);
        _wmaFinal = new WMA(_sqrtPeriod);
    }

    public override void Update(decimal price)
    {
        _count++;
        _wmaHalf.Update(price);
        _wmaFull.Update(price);

        // Calculate intermediate: 2*WMA(n/2) - WMA(n)
        var intermediate = 2m * _wmaHalf.Value - _wmaFull.Value;

        // Apply final WMA smoothing
        _wmaFinal.Update(intermediate);
        _value = _wmaFinal.Value;
    }

    public override void Reset()
    {
        base.Reset();
        _wmaHalf.Reset();
        _wmaFull.Reset();
        _wmaFinal.Reset();
    }
}
