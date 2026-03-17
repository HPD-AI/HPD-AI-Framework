namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Variable Index Dynamic Average.
/// O(1) update, zero allocations.
/// Adapts EMA smoothing based on CMO.
/// </summary>
public sealed class VIDYA : PriceIndicatorBase
{
    private readonly int _period;
    private readonly CMO _cmo;
    private readonly decimal _sc;

    public override bool IsReady => _count >= _period && _cmo.IsReady;

    public VIDYA(int period, int cmoPeriod = 9)
    {
        _period = period;
        _cmo = new CMO(cmoPeriod);
        _sc = 2m / (period + 1);
    }

    public override void Update(decimal price)
    {
        _count++;

        _cmo.Update(price);

        if (_count == 1)
        {
            _value = price;
        }
        else
        {
            var cmoValue = Math.Abs(_cmo.Value) / 100m;
            var alpha = _sc * cmoValue;
            _value = alpha * price + (1m - alpha) * _value;
        }
    }

    public override void Reset()
    {
        base.Reset();
        _cmo.Reset();
    }
}
