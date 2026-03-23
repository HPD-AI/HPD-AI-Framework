namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Maximum value over period.
/// O(1) update using circular buffer.
/// </summary>
public sealed class Max : PriceIndicatorBase
{
    private readonly int _period;
    private readonly decimal[] _buffer;
    private int _index;

    public override bool IsReady => _count >= _period;

    public Max(int period)
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

        if (IsReady)
        {
            _value = decimal.MinValue;
            for (int i = 0; i < _period; i++)
            {
                if (_buffer[i] > _value)
                    _value = _buffer[i];
            }
        }
        else
        {
            if (price > _value || _count == 1)
                _value = price;
        }
    }

    public override void Reset()
    {
        base.Reset();
        _index = 0;
        Array.Clear(_buffer);
    }
}
