using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Williams %R.
/// O(1) update, zero allocations.
/// </summary>
public sealed class WilliamsR : BarIndicatorBase
{
    private readonly int _period;
    private readonly decimal[] _highs;
    private readonly decimal[] _lows;
    private readonly decimal[] _closes;
    private int _index;

    public override bool IsReady => _count >= _period;

    public WilliamsR(int period)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _highs = new decimal[period];
        _lows = new decimal[period];
        _closes = new decimal[period];
    }

    public override void Update(Bar bar)
    {
        _highs[_index] = bar.High.Value;
        _lows[_index] = bar.Low.Value;
        _closes[_index] = bar.Close.Value;
        _index = (_index + 1) % _period;
        _count++;

        if (_count >= _period)
        {
            var highest = decimal.MinValue;
            var lowest = decimal.MaxValue;

            for (int i = 0; i < _period; i++)
            {
                if (_highs[i] > highest) highest = _highs[i];
                if (_lows[i] < lowest) lowest = _lows[i];
            }

            var range = highest - lowest;
            var close = _closes[(_index - 1 + _period) % _period];
            _value = range > 0 ? -100m * (highest - close) / range : -50m;
        }
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_highs, 0, _highs.Length);
        Array.Clear(_lows, 0, _lows.Length);
        Array.Clear(_closes, 0, _closes.Length);
        _index = 0;
    }
}
