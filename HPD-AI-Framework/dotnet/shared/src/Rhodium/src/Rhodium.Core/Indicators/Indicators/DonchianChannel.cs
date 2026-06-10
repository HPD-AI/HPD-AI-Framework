using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Donchian Channel.
/// O(1) update, zero allocations.
/// </summary>
public sealed class DonchianChannel : BarIndicatorBase
{
    private readonly int _period;
    private readonly decimal[] _highs;
    private readonly decimal[] _lows;
    private int _index;

    public decimal Upper { get; private set; }
    public decimal Middle { get; private set; }
    public decimal Lower { get; private set; }

    public override bool IsReady => _count >= _period;

    public DonchianChannel(int period)
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
            var highest = decimal.MinValue;
            var lowest = decimal.MaxValue;

            for (int i = 0; i < _period; i++)
            {
                if (_highs[i] > highest) highest = _highs[i];
                if (_lows[i] < lowest) lowest = _lows[i];
            }

            Upper = highest;
            Lower = lowest;
            Middle = (highest + lowest) / 2m;
            _value = Middle;
        }
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_highs, 0, _highs.Length);
        Array.Clear(_lows, 0, _lows.Length);
        _index = 0;
        Upper = 0m;
        Middle = 0m;
        Lower = 0m;
    }
}
