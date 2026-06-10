namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Standard Deviation.
/// O(1) update using Welford's online algorithm, zero allocations.
/// </summary>
public sealed class StdDev : PriceIndicatorBase
{
    private readonly int _period;
    private readonly decimal[] _buffer;
    private int _index;
    private decimal _sum;
    private decimal _sumSq;

    public override bool IsReady => _count >= _period;

    public StdDev(int period)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _buffer = new decimal[period];
    }

    public override void Update(decimal price)
    {
        var oldValue = _buffer[_index];
        _buffer[_index] = price;
        _index = (_index + 1) % _period;
        _count++;

        // Update sum
        _sum += price;
        if (_count > _period)
            _sum -= oldValue;

        // Update sum of squares
        _sumSq += price * price;
        if (_count > _period)
            _sumSq -= oldValue * oldValue;

        // Calculate standard deviation
        if (_count >= _period)
        {
            var n = (decimal)_period;
            var mean = _sum / n;
            var variance = (_sumSq / n) - (mean * mean);
            _value = variance > 0 ? (decimal)Math.Sqrt((double)variance) : 0m;
        }
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_buffer, 0, _buffer.Length);
        _index = 0;
        _sum = 0m;
        _sumSq = 0m;
    }
}
