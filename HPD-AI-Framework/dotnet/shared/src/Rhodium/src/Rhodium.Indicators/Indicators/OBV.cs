using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming On Balance Volume.
/// O(1) update, zero allocations.
/// </summary>
public sealed class OBV : BarIndicatorBase
{
    private decimal _prevClose;

    public override bool IsReady => _count > 1;

    public override void Update(Bar bar)
    {
        _count++;

        if (_count == 1)
        {
            _prevClose = bar.Close.Value;
            _value = 0m;
            return;
        }

        if (bar.Close.Value > _prevClose)
            _value += bar.Volume.Value;
        else if (bar.Close.Value < _prevClose)
            _value -= bar.Volume.Value;
        // If close == prevClose, OBV doesn't change

        _prevClose = bar.Close.Value;
    }

    public override void Reset()
    {
        base.Reset();
        _prevClose = 0m;
    }
}
