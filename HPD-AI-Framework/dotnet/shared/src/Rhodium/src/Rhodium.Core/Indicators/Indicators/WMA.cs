namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Weighted Moving Average.
/// O(1) update, zero allocations.
/// Recent values get higher weight.
/// </summary>
public sealed class WMA : PriceIndicatorBase
{
    private readonly int _period;
    private readonly decimal[] _buffer;
    private readonly decimal _weightSum;
    private int _index;

    public override bool IsReady => _count >= _period;

    public WMA(int period)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _buffer = new decimal[period];

        // Calculate weight sum: 1 + 2 + 3 + ... + period
        _weightSum = period * (period + 1) / 2m;
    }

    public override void Update(decimal price)
    {
        _buffer[_index] = price;
        _index = (_index + 1) % _period;
        _count++;

        if (_count >= _period)
        {
            var sum = 0m;
            for (int i = 0; i < _period; i++)
            {
                var bufferIndex = (_index + i) % _period;
                var weight = i + 1; // Recent values get higher weight
                sum += _buffer[bufferIndex] * weight;
            }
            _value = sum / _weightSum;
        }
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_buffer, 0, _buffer.Length);
        _index = 0;
    }
}
