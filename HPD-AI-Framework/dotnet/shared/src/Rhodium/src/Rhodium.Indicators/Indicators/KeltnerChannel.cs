using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Keltner Channel.
/// O(1) update, zero allocations.
/// </summary>
public sealed class KeltnerChannel : BarIndicatorBase
{
    private readonly EMA _ema;
    private readonly ATR _atr;
    private readonly decimal _multiplier;

    public decimal Upper { get; private set; }
    public decimal Middle { get; private set; }
    public decimal Lower { get; private set; }

    public override bool IsReady => _ema.IsReady && _atr.IsReady;

    public KeltnerChannel(int period, decimal multiplier = 2m)
    {
        _ema = new EMA(period);
        _atr = new ATR(period);
        _multiplier = multiplier;
    }

    public override void Update(Bar bar)
    {
        _count++;
        _ema.Update(bar.Close.Value);
        _atr.Update(bar);

        Middle = _ema.Value;
        var offset = _atr.Value * _multiplier;
        Upper = Middle + offset;
        Lower = Middle - offset;

        _value = Middle;
    }

    public override void Reset()
    {
        base.Reset();
        _ema.Reset();
        _atr.Reset();
        Upper = 0m;
        Middle = 0m;
        Lower = 0m;
    }
}
