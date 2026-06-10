namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Advanced Moving Average Trend.
/// O(1) update, zero allocations.
/// Returns trend direction and strength.
/// </summary>
public sealed class AMAT : PriceIndicatorBase
{
    private readonly EMA _fast;
    private readonly EMA _medium;
    private readonly EMA _slow;

    public int Direction { get; private set; }
    public decimal Strength { get; private set; }

    public override bool IsReady => _fast.IsReady && _medium.IsReady && _slow.IsReady;

    public AMAT(int fastPeriod = 8, int mediumPeriod = 21, int slowPeriod = 55)
    {
        _fast = new EMA(fastPeriod);
        _medium = new EMA(mediumPeriod);
        _slow = new EMA(slowPeriod);
    }

    public override void Update(decimal price)
    {
        _count++;
        _fast.Update(price);
        _medium.Update(price);
        _slow.Update(price);

        var fast = _fast.Value;
        var medium = _medium.Value;
        var slow = _slow.Value;

        var bullishAlignment = fast > medium && medium > slow;
        var bearishAlignment = fast < medium && medium < slow;

        var range = Math.Max(fast, Math.Max(medium, slow)) - Math.Min(fast, Math.Min(medium, slow));
        var avgPrice = (fast + medium + slow) / 3m;
        var strength = avgPrice > 0 ? Math.Min(1m, range / avgPrice * 20m) : 0m;

        if (bullishAlignment)
        {
            Direction = 1;
            Strength = strength;
        }
        else if (bearishAlignment)
        {
            Direction = -1;
            Strength = strength;
        }
        else
        {
            Direction = 0;
            Strength = strength * 0.5m;
        }

        _value = Direction * Strength;
    }

    public override void Reset()
    {
        base.Reset();
        _fast.Reset();
        _medium.Reset();
        _slow.Reset();
        Direction = 0;
        Strength = 0m;
    }
}
