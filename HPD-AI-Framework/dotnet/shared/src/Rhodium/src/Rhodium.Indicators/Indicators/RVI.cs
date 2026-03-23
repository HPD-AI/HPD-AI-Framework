namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Relative Volatility Index.
/// O(1) update, zero allocations.
/// RSI applied to standard deviation.
/// </summary>
public sealed class RVI : PriceIndicatorBase
{
    private readonly int _period;
    private readonly int _stdPeriod;
    private readonly StdDev _stdDev;
    private readonly RMA _avgGain;
    private readonly RMA _avgLoss;
    private decimal _prevStd;

    public override bool IsReady => _count > _stdPeriod + _period;

    public RVI(int period = 14, int stdPeriod = 10)
    {
        _period = period;
        _stdPeriod = stdPeriod;
        _stdDev = new StdDev(stdPeriod);
        _avgGain = new RMA(period);
        _avgLoss = new RMA(period);
    }

    public override void Update(decimal price)
    {
        _count++;
        _stdDev.Update(price);

        if (_count <= _stdPeriod)
        {
            _prevStd = _stdDev.Value;
            return;
        }

        var currentStd = _stdDev.Value;
        var change = currentStd - _prevStd;
        var gain = change > 0 ? change : 0m;
        var loss = change < 0 ? -change : 0m;

        _avgGain.Update(gain);
        _avgLoss.Update(loss);

        if (_avgLoss.Value == 0)
        {
            _value = 100m;
        }
        else
        {
            var rs = _avgGain.Value / _avgLoss.Value;
            _value = 100m - (100m / (1m + rs));
        }

        _prevStd = currentStd;
    }

    public override void Reset()
    {
        base.Reset();
        _stdDev.Reset();
        _avgGain.Reset();
        _avgLoss.Reset();
        _prevStd = 0m;
    }
}
