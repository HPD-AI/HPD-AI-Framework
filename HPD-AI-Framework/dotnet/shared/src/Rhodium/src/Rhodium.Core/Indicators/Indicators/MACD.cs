namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming MACD (Moving Average Convergence Divergence).
/// O(1) update, zero allocations.
/// </summary>
public sealed class MACD : PriceIndicatorBase
{
    private readonly EMA _fastEma;
    private readonly EMA _slowEma;
    private readonly EMA _signalEma;
    private readonly int _signalPeriod;

    public decimal Signal { get; private set; }
    public decimal Histogram { get; private set; }
    public override bool IsReady => _fastEma.IsReady && _slowEma.IsReady && _signalEma.IsReady;

    public MACD(int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
    {
        _fastEma = new EMA(fastPeriod);
        _slowEma = new EMA(slowPeriod);
        _signalEma = new EMA(signalPeriod);
        _signalPeriod = signalPeriod;
    }

    public override void Update(decimal price)
    {
        _count++;

        _fastEma.Update(price);
        _slowEma.Update(price);

        // MACD line = fast EMA - slow EMA
        _value = _fastEma.Value - _slowEma.Value;

        // Signal line = EMA of MACD line
        _signalEma.Update(_value);
        Signal = _signalEma.Value;

        // Histogram = MACD - Signal
        Histogram = _value - Signal;
    }

    public override void Reset()
    {
        base.Reset();
        _fastEma.Reset();
        _slowEma.Reset();
        _signalEma.Reset();
        Signal = 0m;
        Histogram = 0m;
    }
}
