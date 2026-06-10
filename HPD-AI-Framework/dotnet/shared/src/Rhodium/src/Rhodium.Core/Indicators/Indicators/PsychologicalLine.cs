namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Psychological Line.
/// O(1) update, zero allocations.
/// Measures percentage of bars closing above previous close.
/// </summary>
public sealed class PsychologicalLine : PriceIndicatorBase
{
    private readonly int _period;
    private readonly bool[] _isUp;
    private int _index;
    private int _upCount;
    private decimal _prevPrice;

    public override bool IsReady => _count > _period;

    public PsychologicalLine(int period)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _isUp = new bool[period];
    }

    public override void Update(decimal price)
    {
        _count++;

        if (_count == 1)
        {
            _prevPrice = price;
            return;
        }

        var isUpNow = price > _prevPrice;
        var oldIsUp = _isUp[_index];

        _isUp[_index] = isUpNow;
        _index = (_index + 1) % _period;

        if (isUpNow) _upCount++;
        if (_count > _period && oldIsUp) _upCount--;

        if (_count > _period)
        {
            _value = (decimal)_upCount / _period * 100m;
        }

        _prevPrice = price;
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_isUp, 0, _isUp.Length);
        _index = 0;
        _upCount = 0;
        _prevPrice = 0m;
    }
}
