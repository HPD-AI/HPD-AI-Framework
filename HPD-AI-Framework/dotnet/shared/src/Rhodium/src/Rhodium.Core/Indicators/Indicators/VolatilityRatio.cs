using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Volatility Ratio.
/// O(1) update, zero allocations.
/// Ratio of current TR to ATR.
/// </summary>
public sealed class VolatilityRatio : BarIndicatorBase
{
    private readonly ATR _atr;
    private decimal _prevClose;
    private decimal _currentTR;

    public override bool IsReady => _count > 1 && _atr.IsReady;

    public VolatilityRatio(int period)
    {
        _atr = new ATR(period);
    }

    public override void Update(Bar bar)
    {
        _count++;

        _atr.Update(bar);

        if (_count == 1)
        {
            _prevClose = bar.Close.Value;
            return;
        }

        // Calculate current true range
        _currentTR = Math.Max(
            bar.High.Value - bar.Low.Value,
            Math.Max(
                Math.Abs(bar.High.Value - _prevClose),
                Math.Abs(bar.Low.Value - _prevClose)
            )
        );

        _value = _atr.Value > 0 ? _currentTR / _atr.Value : 1m;
        _prevClose = bar.Close.Value;
    }

    public override void Reset()
    {
        base.Reset();
        _atr.Reset();
        _prevClose = 0m;
        _currentTR = 0m;
    }
}
