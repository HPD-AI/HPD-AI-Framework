using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Pivot Points.
/// O(1) update, zero allocations.
/// </summary>
public sealed class PivotPoints : BarIndicatorBase
{
    public decimal PP { get; private set; }
    public decimal S1 { get; private set; }
    public decimal S2 { get; private set; }
    public decimal R1 { get; private set; }
    public decimal R2 { get; private set; }

    public override bool IsReady => _count > 0;

    public override void Update(Bar bar)
    {
        _count++;

        var high = bar.High.Value;
        var low = bar.Low.Value;
        var close = bar.Close.Value;

        PP = (high + low + close) / 3m;
        R1 = 2m * PP - low;
        S1 = 2m * PP - high;
        R2 = PP + (high - low);
        S2 = PP - (high - low);

        _value = PP;
    }

    public override void Reset()
    {
        base.Reset();
        PP = 0m;
        S1 = 0m;
        S2 = 0m;
        R1 = 0m;
        R2 = 0m;
    }
}
