using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Accumulation/Distribution.
/// O(1) update, zero allocations.
/// </summary>
public sealed class AD : BarIndicatorBase
{
    public override bool IsReady => _count > 0;

    public override void Update(Bar bar)
    {
        _count++;

        var hl = bar.High.Value - bar.Low.Value;
        var clv = hl > 0
            ? ((bar.Close.Value - bar.Low.Value) - (bar.High.Value - bar.Close.Value)) / hl
            : 0m;

        _value += clv * bar.Volume.Value;
    }
}
