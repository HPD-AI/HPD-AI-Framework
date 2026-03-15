using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Aroon Oscillator (Aroon Up - Aroon Down).
/// O(1) update, zero allocations.
/// Range: -100 to +100.
/// </summary>
public sealed class AroonOsc : BarIndicatorBase
{
    private readonly Aroon _aroon;

    public override bool IsReady => _aroon.IsReady;

    public decimal Up => _aroon.Up;
    public decimal Down => _aroon.Down;

    public AroonOsc(int period)
    {
        _aroon = new Aroon(period);
    }

    public override void Update(Bar bar)
    {
        _aroon.Update(bar);
        _count = _aroon.Count;
        _value = _aroon.Value; // Aroon already computes Up - Down
    }

    public override void Reset()
    {
        base.Reset();
        _aroon.Reset();
    }
}
