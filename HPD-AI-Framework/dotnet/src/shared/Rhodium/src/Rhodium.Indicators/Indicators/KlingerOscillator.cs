using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Klinger Volume Oscillator.
/// O(1) update, zero allocations.
/// Combines price movement and volume to predict reversals.
/// </summary>
public sealed class KlingerOscillator : BarIndicatorBase
{
    private readonly EMA _fastEma;
    private readonly EMA _slowEma;
    private readonly EMA _signalEma;
    private decimal _prevHLC;
    private decimal _cm;
    private int _trend = 1;

    public decimal Signal { get; private set; }

    public override bool IsReady => _fastEma.IsReady && _slowEma.IsReady && _signalEma.IsReady;

    public KlingerOscillator(int fastPeriod = 34, int slowPeriod = 55, int signalPeriod = 13)
    {
        _fastEma = new EMA(fastPeriod);
        _slowEma = new EMA(slowPeriod);
        _signalEma = new EMA(signalPeriod);
    }

    public override void Update(Bar bar)
    {
        _count++;

        var hlc = bar.High.Value + bar.Low.Value + bar.Close.Value;

        if (_count == 1)
        {
            _prevHLC = hlc;
            return;
        }

        var currentTrend = hlc > _prevHLC ? 1 : -1;
        var dm = bar.High.Value - bar.Low.Value;

        if (currentTrend == _trend)
            _cm += dm;
        else
        {
            _cm = dm;
            _trend = currentTrend;
        }

        var vf = _cm != 0 ? bar.Volume.Value * Math.Abs(2m * (dm / _cm) - 1m) * _trend * 100m : 0m;

        _fastEma.Update(vf);
        _slowEma.Update(vf);

        _value = _fastEma.Value - _slowEma.Value;
        _signalEma.Update(_value);
        Signal = _signalEma.Value;

        _prevHLC = hlc;
    }

    public override void Reset()
    {
        base.Reset();
        _fastEma.Reset();
        _slowEma.Reset();
        _signalEma.Reset();
        _prevHLC = 0m;
        _cm = 0m;
        _trend = 1;
        Signal = 0m;
    }
}
