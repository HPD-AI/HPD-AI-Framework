namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Detrended Price Oscillator.
/// O(1) update, zero allocations.
/// </summary>
public sealed class DPO : PriceIndicatorBase
{
    private readonly int _period;
    private readonly int _shift;
    private readonly SMA _sma;
    private readonly decimal[] _buffer;
    private int _index;

    public override bool IsReady => _count >= _period + _shift;

    public DPO(int period)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _shift = period / 2 + 1;
        _sma = new SMA(period);
        _buffer = new decimal[period + _shift];
    }

    public override void Update(decimal price)
    {
        _buffer[_index] = price;
        _index = (_index + 1) % _buffer.Length;
        _count++;

        _sma.Update(price);

        if (_count >= _period + _shift)
        {
            var shiftedIndex = (_index - _shift + _buffer.Length) % _buffer.Length;
            var shiftedPrice = _buffer[shiftedIndex];
            _value = shiftedPrice - _sma.Value;
        }
    }

    public override void Reset()
    {
        base.Reset();
        _sma.Reset();
        Array.Clear(_buffer, 0, _buffer.Length);
        _index = 0;
    }
}
