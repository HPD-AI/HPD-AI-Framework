using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Aroon Up and Down.
/// O(1) update, zero allocations.
/// </summary>
public sealed class Aroon : BarIndicatorBase
{
    private readonly int _period;
    private readonly decimal[] _highs;
    private readonly decimal[] _lows;
    private int _index;

    public decimal Up { get; private set; }
    public decimal Down { get; private set; }

    public override bool IsReady => _count >= _period;

    public Aroon(int period)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _highs = new decimal[period];
        _lows = new decimal[period];
    }

    public override void Update(Bar bar)
    {
        _highs[_index] = bar.High.Value;
        _lows[_index] = bar.Low.Value;
        _index = (_index + 1) % _period;
        _count++;

        if (_count >= _period)
        {
            var highIdx = 0;
            var lowIdx = 0;
            var highest = decimal.MinValue;
            var lowest = decimal.MaxValue;

            for (int i = 0; i < _period; i++)
            {
                var idx = (_index + i) % _period;
                if (_highs[idx] >= highest)
                {
                    highest = _highs[idx];
                    highIdx = i;
                }
                if (_lows[idx] <= lowest)
                {
                    lowest = _lows[idx];
                    lowIdx = i;
                }
            }

            Up = 100m * (highIdx + 1) / _period;
            Down = 100m * (lowIdx + 1) / _period;
            _value = Up - Down; // Aroon Oscillator
        }
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_highs, 0, _highs.Length);
        Array.Clear(_lows, 0, _lows.Length);
        _index = 0;
        Up = 0m;
        Down = 0m;
    }
}
