namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Bollinger Bands.
/// O(1) update, zero allocations.
/// Returns Upper, Middle, and Lower bands.
/// </summary>
public sealed class BollingerBands : PriceIndicatorBase
{
    private readonly SMA _sma;
    private readonly StdDev _stdDev;
    private readonly decimal _multiplier;

    public decimal Upper { get; private set; }
    public decimal Middle { get; private set; }
    public decimal Lower { get; private set; }

    public override bool IsReady => _sma.IsReady && _stdDev.IsReady;

    public BollingerBands(int period = 20, decimal multiplier = 2m)
    {
        _sma = new SMA(period);
        _stdDev = new StdDev(period);
        _multiplier = multiplier;
    }

    public override void Update(decimal price)
    {
        _count++;
        _sma.Update(price);
        _stdDev.Update(price);

        Middle = _sma.Value;
        var offset = _stdDev.Value * _multiplier;
        Upper = Middle + offset;
        Lower = Middle - offset;

        // Value returns middle band
        _value = Middle;
    }

    public override void Reset()
    {
        base.Reset();
        _sma.Reset();
        _stdDev.Reset();
        Upper = 0m;
        Middle = 0m;
        Lower = 0m;
    }
}
