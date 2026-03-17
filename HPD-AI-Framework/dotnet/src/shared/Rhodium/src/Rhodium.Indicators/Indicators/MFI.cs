using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Money Flow Index.
/// O(1) update, zero allocations.
/// </summary>
public sealed class MFI : BarIndicatorBase
{
    private readonly int _period;
    private readonly decimal[] _posFlows;
    private readonly decimal[] _negFlows;
    private int _index;
    private decimal _sumPos;
    private decimal _sumNeg;
    private decimal _prevTypical;

    public override bool IsReady => _count > _period;

    public MFI(int period)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _posFlows = new decimal[period];
        _negFlows = new decimal[period];
    }

    public override void Update(Bar bar)
    {
        var typical = bar.Typical.Value;
        var volume = bar.Volume.Value;
        _count++;

        if (_count == 1)
        {
            _prevTypical = typical;
            return;
        }

        var moneyFlow = typical * volume;
        decimal posFlow = 0m, negFlow = 0m;

        if (typical > _prevTypical)
            posFlow = moneyFlow;
        else if (typical < _prevTypical)
            negFlow = moneyFlow;

        var oldPos = _posFlows[_index];
        var oldNeg = _negFlows[_index];

        _posFlows[_index] = posFlow;
        _negFlows[_index] = negFlow;
        _index = (_index + 1) % _period;

        _sumPos += posFlow;
        _sumNeg += negFlow;

        if (_count > _period)
        {
            _sumPos -= oldPos;
            _sumNeg -= oldNeg;
        }

        if (_count > _period)
        {
            _value = _sumNeg == 0 ? 100m : 100m - (100m / (1m + _sumPos / _sumNeg));
        }

        _prevTypical = typical;
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_posFlows, 0, _posFlows.Length);
        Array.Clear(_negFlows, 0, _negFlows.Length);
        _index = 0;
        _sumPos = 0m;
        _sumNeg = 0m;
        _prevTypical = 0m;
    }
}
