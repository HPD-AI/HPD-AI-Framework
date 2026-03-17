namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Linear Regression Slope.
/// O(1) update using circular buffer.
/// </summary>
public sealed class LinearRegSlope : PriceIndicatorBase
{
    private readonly int _period;
    private readonly decimal[] _buffer;
    private int _index;

    public override bool IsReady => _count >= _period;

    public LinearRegSlope(int period)
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
            var sumX = 0m;
            var sumY = 0m;
            var sumXY = 0m;
            var sumX2 = 0m;

            for (int i = 0; i < _period; i++)
            {
                var x = i;
                var bufIdx = (_index + i) % _period;
                var y = _buffer[bufIdx];
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumX2 += x * x;
            }

            var denominator = _period * sumX2 - sumX * sumX;
            _value = denominator != 0 ? (_period * sumXY - sumX * sumY) / denominator : 0;
        }
    }

    public override void Reset()
    {
        base.Reset();
        _index = 0;
        Array.Clear(_buffer);
    }
}
