using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Commodity Channel Index.
/// O(1) update, zero allocations.
/// </summary>
public sealed class CCI : BarIndicatorBase
{
    private readonly int _period;
    private readonly decimal _constant;
    private readonly decimal[] _typicals;
    private int _index;
    private decimal _sum;

    public override bool IsReady => _count >= _period;

    public CCI(int period, decimal constant = 0.015m)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _constant = constant;
        _typicals = new decimal[period];
    }

    public override void Update(Bar bar)
    {
        var typical = bar.Typical.Value;
        var oldValue = _typicals[_index];

        _typicals[_index] = typical;
        _index = (_index + 1) % _period;
        _count++;

        // Update sum
        _sum += typical;
        if (_count > _period)
            _sum -= oldValue;

        if (_count >= _period)
        {
            var sma = _sum / _period;

            // Calculate mean absolute deviation
            var mad = 0m;
            for (int i = 0; i < _period; i++)
                mad += Math.Abs(_typicals[i] - sma);
            mad /= _period;

            _value = mad > 0 ? (typical - sma) / (_constant * mad) : 0m;
        }
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_typicals, 0, _typicals.Length);
        _index = 0;
        _sum = 0m;
    }
}
