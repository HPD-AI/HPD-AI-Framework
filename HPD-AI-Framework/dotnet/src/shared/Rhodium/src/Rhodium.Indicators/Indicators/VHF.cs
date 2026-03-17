namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Vertical Horizontal Filter.
/// O(1) update, zero allocations.
/// Measures trend strength.
/// </summary>
public sealed class VHF : PriceIndicatorBase
{
    private readonly int _period;
    private readonly decimal[] _buffer;
    private int _index;

    public override bool IsReady => _count >= _period;

    public VHF(int period)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _buffer = new decimal[period];
    }

    public override void Update(decimal price)
    {
        _buffer[_index] = price;
        _index = (_index + 1) % _period;
        _count++;

        if (_count >= _period)
        {
            var highest = decimal.MinValue;
            var lowest = decimal.MaxValue;
            var sumAbsChanges = 0m;

            for (int i = 0; i < _period; i++)
            {
                var idx = (_index + i) % _period;
                if (_buffer[idx] > highest) highest = _buffer[idx];
                if (_buffer[idx] < lowest) lowest = _buffer[idx];

                if (i > 0)
                {
                    var prevIdx = (_index + i - 1) % _period;
                    sumAbsChanges += Math.Abs(_buffer[idx] - _buffer[prevIdx]);
                }
            }

            _value = sumAbsChanges > 0 ? (highest - lowest) / sumAbsChanges : 0m;
        }
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_buffer, 0, _buffer.Length);
        _index = 0;
    }
}
