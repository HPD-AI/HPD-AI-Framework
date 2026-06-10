namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Simple Moving Average using circular buffer.
/// O(1) update, O(period) memory.
/// </summary>
public sealed class SMA : PriceIndicatorBase
{
    private readonly int _period;
    private readonly decimal[] _buffer;
    private int _index;
    private decimal _sum;

    public override bool IsReady => _count >= _period;

    public SMA(int period)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _buffer = new decimal[period];
    }

    public override void Update(decimal price)
    {
        // Remove oldest value from sum
        if (_count >= _period)
            _sum -= _buffer[_index];

        // Add new value
        _buffer[_index] = price;
        _sum += price;
        _count++;

        // Circular buffer index
        _index = (_index + 1) % _period;

        // Calculate average
        _value = _sum / Math.Min(_count, _period);
    }

    public override void Reset()
    {
        base.Reset();
        _sum = 0m;
        _index = 0;
        Array.Clear(_buffer);
    }
}
