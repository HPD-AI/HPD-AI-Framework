using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Average True Range.
/// O(1) update, zero allocations.
/// </summary>
public sealed class ATR : BarIndicatorBase
{
    private readonly RMA _rma;
    private decimal _prevClose;

    public override bool IsReady => _rma.IsReady;

    public ATR(int period)
    {
        _rma = new RMA(period);
    }

    public override void Update(Bar bar)
    {
        _count++;

        if (_count == 1)
        {
            _prevClose = bar.Close.Value;
            _value = 0m;
            return;
        }

        // True Range = max(high - low, |high - prevClose|, |low - prevClose|)
        var tr = Math.Max(
            bar.High.Value - bar.Low.Value,
            Math.Max(
                Math.Abs(bar.High.Value - _prevClose),
                Math.Abs(bar.Low.Value - _prevClose)
            )
        );

        _rma.Update(tr);
        _value = _rma.Value;
        _prevClose = bar.Close.Value;
    }

    public override void Reset()
    {
        base.Reset();
        _rma.Reset();
        _prevClose = 0m;
    }
}
