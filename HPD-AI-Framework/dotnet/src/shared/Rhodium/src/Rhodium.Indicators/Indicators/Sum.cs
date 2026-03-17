namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Sum over period.
/// O(1) update using circular buffer.
/// </summary>
public sealed class Sum : PriceIndicatorBase
{
    private readonly int _period;
    private readonly decimal[] _buffer;
    private int _index;
    private decimal _sum;

    public override bool IsReady => _count >= _period;

    public Sum(int period)
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
        _index = (_index + 1) % _period;
        _count++;

        _value = _sum;
    }

    public override void Reset()
    {
        base.Reset();
        _sum = 0m;
        _index = 0;
        Array.Clear(_buffer);
    }
}
