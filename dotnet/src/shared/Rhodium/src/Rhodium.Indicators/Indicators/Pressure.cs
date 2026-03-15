using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Buy/Sell Pressure.
/// O(1) update, zero allocations.
/// Measures balance between buying and selling pressure.
/// </summary>
public sealed class Pressure : BarIndicatorBase
{
    private readonly int _period;
    private readonly decimal[] _buyPressures;
    private readonly decimal[] _sellPressures;
    private int _index;
    private decimal _sumBuy;
    private decimal _sumSell;

    public override bool IsReady => _count >= _period;

    public Pressure(int period)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));

        _period = period;
        _buyPressures = new decimal[period];
        _sellPressures = new decimal[period];
    }

    public override void Update(Bar bar)
    {
        var range = bar.High.Value - bar.Low.Value;
        decimal buyPressure = 0m, sellPressure = 0m;

        if (range > 0)
        {
            buyPressure = (bar.Close.Value - bar.Low.Value) * bar.Volume.Value;
            sellPressure = (bar.High.Value - bar.Close.Value) * bar.Volume.Value;
        }

        var oldBuy = _buyPressures[_index];
        var oldSell = _sellPressures[_index];

        _buyPressures[_index] = buyPressure;
        _sellPressures[_index] = sellPressure;
        _index = (_index + 1) % _period;
        _count++;

        _sumBuy += buyPressure;
        _sumSell += sellPressure;

        if (_count > _period)
        {
            _sumBuy -= oldBuy;
            _sumSell -= oldSell;
        }

        if (_count >= _period)
        {
            var total = _sumBuy + _sumSell;
            _value = total > 0 ? (_sumBuy - _sumSell) / total * 100m : 0m;
        }
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_buyPressures, 0, _buyPressures.Length);
        Array.Clear(_sellPressures, 0, _sellPressures.Length);
        _index = 0;
        _sumBuy = 0m;
        _sumSell = 0m;
    }
}
