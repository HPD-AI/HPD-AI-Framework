namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Percentage Price Oscillator.
/// O(1) update, zero allocations.
/// </summary>
public sealed class PPO : PriceIndicatorBase
{
    private readonly EMA _fastEma;
    private readonly EMA _slowEma;

    public override bool IsReady => _fastEma.IsReady && _slowEma.IsReady;

    public PPO(int fastPeriod = 12, int slowPeriod = 26)
    {
        _fastEma = new EMA(fastPeriod);
        _slowEma = new EMA(slowPeriod);
    }

    public override void Update(decimal price)
    {
        _count++;
        _fastEma.Update(price);
        _slowEma.Update(price);

        var slowValue = _slowEma.Value;
        _value = slowValue != 0 ? 100m * (_fastEma.Value - slowValue) / slowValue : 0m;
    }

    public override void Reset()
    {
        base.Reset();
        _fastEma.Reset();
        _slowEma.Reset();
    }
}
