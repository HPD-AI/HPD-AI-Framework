namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Chande Momentum Oscillator.
/// O(1) update, zero allocations.
/// </summary>
public sealed class CMO : PriceIndicatorBase
{
    private readonly int _period;
    private readonly decimal[] _ups;
    private readonly decimal[] _downs;
    private int _index;
    private decimal _sumUp;
    private decimal _sumDown;
    private decimal _prevPrice;

    public override bool IsReady => _count > _period;

    public CMO(int period)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _ups = new decimal[period];
        _downs = new decimal[period];
    }

    public override void Update(decimal price)
    {
        _count++;

        if (_count == 1)
        {
            _prevPrice = price;
            return;
        }

        var change = price - _prevPrice;
        var up = change > 0 ? change : 0m;
        var down = change < 0 ? -change : 0m;

        var oldUp = _ups[_index];
        var oldDown = _downs[_index];

        _ups[_index] = up;
        _downs[_index] = down;
        _index = (_index + 1) % _period;

        _sumUp += up;
        _sumDown += down;

        if (_count > _period)
        {
            _sumUp -= oldUp;
            _sumDown -= oldDown;
        }

        if (_count > _period)
        {
            var total = _sumUp + _sumDown;
            _value = total > 0 ? 100m * (_sumUp - _sumDown) / total : 0m;
        }

        _prevPrice = price;
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_ups, 0, _ups.Length);
        Array.Clear(_downs, 0, _downs.Length);
        _index = 0;
        _sumUp = 0m;
        _sumDown = 0m;
        _prevPrice = 0m;
    }
}
