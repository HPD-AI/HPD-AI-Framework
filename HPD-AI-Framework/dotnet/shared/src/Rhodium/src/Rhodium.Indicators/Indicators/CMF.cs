using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Chaikin Money Flow.
/// O(1) update, zero allocations.
/// </summary>
public sealed class CMF : BarIndicatorBase
{
    private readonly int _period;
    private readonly decimal[] _adValues;
    private readonly decimal[] _volumes;
    private int _index;
    private decimal _sumAD;
    private decimal _sumVolume;

    public override bool IsReady => _count >= _period;

    public CMF(int period)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _adValues = new decimal[period];
        _volumes = new decimal[period];
    }

    public override void Update(Bar bar)
    {
        var hl = bar.High.Value - bar.Low.Value;
        var clv = hl > 0
            ? ((bar.Close.Value - bar.Low.Value) - (bar.High.Value - bar.Close.Value)) / hl
            : 0m;
        var adValue = clv * bar.Volume.Value;
        var volume = bar.Volume.Value;

        var oldAD = _adValues[_index];
        var oldVolume = _volumes[_index];

        _adValues[_index] = adValue;
        _volumes[_index] = volume;
        _index = (_index + 1) % _period;
        _count++;

        _sumAD += adValue;
        _sumVolume += volume;

        if (_count > _period)
        {
            _sumAD -= oldAD;
            _sumVolume -= oldVolume;
        }

        if (_count >= _period)
        {
            _value = _sumVolume > 0 ? _sumAD / _sumVolume : 0m;
        }
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_adValues, 0, _adValues.Length);
        Array.Clear(_volumes, 0, _volumes.Length);
        _index = 0;
        _sumAD = 0m;
        _sumVolume = 0m;
    }
}
