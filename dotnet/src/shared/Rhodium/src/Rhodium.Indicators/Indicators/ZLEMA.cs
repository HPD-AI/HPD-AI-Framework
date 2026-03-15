namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Zero-Lag Exponential Moving Average.
/// O(1) update, zero allocations.
/// Applies lag reduction to EMA.
/// </summary>
public sealed class ZLEMA : PriceIndicatorBase
{
    private readonly int _period;
    private readonly int _lag;
    private readonly EMA _ema;
    private readonly decimal[] _buffer;
    private int _index;

    public override bool IsReady => _count >= _period;

    public ZLEMA(int period)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _lag = (period - 1) / 2;
        _ema = new EMA(period);
        _buffer = new decimal[_lag + 1];
    }

    public override void Update(decimal price)
    {
        _buffer[_index] = price;
        _count++;

        // Get lagged value
        var laggedIndex = (_index - _lag + _buffer.Length) % _buffer.Length;
        var laggedPrice = _count > _lag ? _buffer[laggedIndex] : price;

        // Adjusted price = 2 * current - lagged
        var adjusted = 2m * price - laggedPrice;

        _ema.Update(adjusted);
        _value = _ema.Value;

        _index = (_index + 1) % _buffer.Length;
    }

    public override void Reset()
    {
        base.Reset();
        _ema.Reset();
        Array.Clear(_buffer, 0, _buffer.Length);
        _index = 0;
    }
}
