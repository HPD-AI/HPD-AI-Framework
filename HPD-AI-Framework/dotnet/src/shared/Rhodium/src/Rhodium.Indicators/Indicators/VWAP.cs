using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Volume Weighted Average Price.
/// O(1) update, zero allocations.
/// </summary>
public sealed class VWAP : BarIndicatorBase
{
    private decimal _cumPriceVolume;
    private decimal _cumVolume;

    public override bool IsReady => _count > 0;

    public override void Update(Bar bar)
    {
        _count++;

        var typical = bar.Typical.Value;
        var volume = bar.Volume.Value;

        _cumPriceVolume += typical * volume;
        _cumVolume += volume;

        _value = _cumVolume > 0 ? _cumPriceVolume / _cumVolume : 0m;
    }

    public override void Reset()
    {
        base.Reset();
        _cumPriceVolume = 0m;
        _cumVolume = 0m;
    }
}
