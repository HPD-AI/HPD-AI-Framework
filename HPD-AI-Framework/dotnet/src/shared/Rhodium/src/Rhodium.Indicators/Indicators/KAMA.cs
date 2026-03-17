namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Kaufman Adaptive Moving Average.
/// O(1) update, zero allocations.
/// Adapts to market conditions.
/// </summary>
public sealed class KAMA : PriceIndicatorBase
{
    private readonly int _period;
    private readonly decimal _fastSC;
    private readonly decimal _slowSC;
    private readonly decimal[] _buffer;
    private int _index;

    public override bool IsReady => _count >= _period;

    public KAMA(int period, int fast = 2, int slow = 30)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _fastSC = 2m / (fast + 1);
        _slowSC = 2m / (slow + 1);
        _buffer = new decimal[period];
    }

    public override void Update(decimal price)
    {
        _buffer[_index] = price;
        _index = (_index + 1) % _period;
        _count++;

        if (_count == 1)
        {
            _value = price;
            return;
        }

        if (_count >= _period)
        {
            var oldestIdx = _index;
            var oldestPrice = _buffer[oldestIdx];
            var change = Math.Abs(price - oldestPrice);

            var volatility = 0m;
            for (int i = 1; i < _period; i++)
            {
                var idx1 = (_index + i - 1) % _period;
                var idx2 = (_index + i) % _period;
                volatility += Math.Abs(_buffer[idx2] - _buffer[idx1]);
            }

            var er = volatility > 0 ? change / volatility : 0m;
            var sc = er * (_fastSC - _slowSC) + _slowSC;
            var scSq = sc * sc;

            _value = _value + scSq * (price - _value);
        }
        else
        {
            _value = price;
        }
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_buffer, 0, _buffer.Length);
        _index = 0;
    }
}
