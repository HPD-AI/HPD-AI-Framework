namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Momentum.
/// O(1) update, zero allocations.
/// </summary>
public sealed class Momentum : PriceIndicatorBase
{
    private readonly int _period;
    private readonly decimal[] _buffer;
    private int _index;

    public override bool IsReady => _count > _period;

    public Momentum(int period)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _buffer = new decimal[period + 1];
    }

    public override void Update(decimal price)
    {
        _buffer[_index] = price;
        _index = (_index + 1) % _buffer.Length;
        _count++;

        if (_count > _period)
        {
            var prevPrice = _buffer[_index];
            _value = price - prevPrice;
        }
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_buffer, 0, _buffer.Length);
        _index = 0;
    }
}
