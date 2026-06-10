namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Efficiency Ratio (Kaufman's ER).
/// Measures trend efficiency: 1 = perfect trend, 0 = no net movement (noise).
/// O(1) update using circular buffer.
/// </summary>
public sealed class EfficiencyRatio : PriceIndicatorBase
{
    private readonly int _period;
    private readonly decimal[] _buffer;
    private int _index;

    public override bool IsReady => _count >= _period;

    public EfficiencyRatio(int period)
    {
        if (period < 2)
            throw new ArgumentException("Period must be >= 2", nameof(period));

        _period = period;
        _buffer = new decimal[period];
    }

    public override void Update(decimal price)
    {
        _buffer[_index] = price;
        _index = (_index + 1) % _period;
        _count++;

        if (IsReady)
        {
            var oldest = _buffer[_index];
            var newest = _buffer[(_index + _period - 1) % _period];
            var netChange = Math.Abs(newest - oldest);

            var sumAbsChanges = 0m;
            for (int i = 1; i < _period; i++)
            {
                var prevIdx = (_index + i - 1) % _period;
                var currIdx = (_index + i) % _period;
                sumAbsChanges += Math.Abs(_buffer[currIdx] - _buffer[prevIdx]);
            }

            _value = sumAbsChanges > 0 ? netChange / sumAbsChanges : 0;
        }
    }

    public override void Reset()
    {
        base.Reset();
        _index = 0;
        Array.Clear(_buffer);
    }
}
