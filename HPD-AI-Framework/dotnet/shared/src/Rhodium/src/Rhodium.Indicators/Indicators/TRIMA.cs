namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Triangular Moving Average.
/// O(1) update, zero allocations.
/// TRIMA = SMA of SMA with proper period calculation.
/// Full implementation with double smoothing.
/// </summary>
public sealed class TRIMA : PriceIndicatorBase
{
    private readonly SMA _sma1;
    private readonly SMA _sma2;
    private readonly int _n;

    public override bool IsReady => _sma1.IsReady && _sma2.IsReady;

    public TRIMA(int period)
    {
        _n = period;

        // For proper TRIMA: use period for first SMA, (period+1)/2 for second
        // This creates triangular weighting
        if (period % 2 == 0)
        {
            // Even period: n/2, n/2 + 1
            var p1 = period / 2;
            var p2 = p1 + 1;
            _sma1 = new SMA(p1);
            _sma2 = new SMA(p2);
        }
        else
        {
            // Odd period: (n+1)/2, (n+1)/2
            var p = (period + 1) / 2;
            _sma1 = new SMA(p);
            _sma2 = new SMA(p);
        }
    }

    public override void Update(decimal price)
    {
        _count++;
        _sma1.Update(price);
        _sma2.Update(_sma1.Value);

        _value = _sma2.Value;
    }

    public override void Reset()
    {
        base.Reset();
        _sma1.Reset();
        _sma2.Reset();
    }
}
