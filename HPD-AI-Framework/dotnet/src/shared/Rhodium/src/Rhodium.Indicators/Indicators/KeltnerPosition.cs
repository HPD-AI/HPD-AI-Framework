using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Keltner Position.
/// O(1) update, zero allocations.
/// Returns relative position within Keltner Channel (0 to 1).
/// </summary>
public sealed class KeltnerPosition : BarIndicatorBase
{
    private readonly KeltnerChannel _keltner;

    public override bool IsReady => _keltner.IsReady;

    public KeltnerPosition(int period, decimal multiplier = 2m)
    {
        _keltner = new KeltnerChannel(period, multiplier);
    }

    public override void Update(Bar bar)
    {
        _count++;
        _keltner.Update(bar);

        var range = _keltner.Upper - _keltner.Lower;
        _value = range > 0 ? (bar.Close.Value - _keltner.Lower) / range : 0.5m;
    }

    public override void Reset()
    {
        base.Reset();
        _keltner.Reset();
    }
}
