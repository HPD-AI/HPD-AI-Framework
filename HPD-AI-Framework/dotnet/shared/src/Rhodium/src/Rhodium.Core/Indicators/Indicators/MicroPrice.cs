using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

public sealed class MicroPrice : TickIndicatorBase
{
    public override bool IsReady => _count > 0;

    public override void Update(in TickFrame tick)
    {
        if (!tick.HasQuote) return;
        _value = tick.MicroPrice;
        _count++;
    }
}
