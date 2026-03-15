namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Relative Strength Index.
/// O(1) update, zero allocations.
/// </summary>
public sealed class RSI : PriceIndicatorBase
{
    private readonly RMA _avgGain;
    private readonly RMA _avgLoss;
    private decimal _prevPrice;

    public override bool IsReady => _avgGain.IsReady && _avgLoss.IsReady;

    public RSI(int period)
    {
        _avgGain = new RMA(period);
        _avgLoss = new RMA(period);
    }

    public override void Update(decimal price)
    {
        _count++;

        if (_count == 1)
        {
            _prevPrice = price;
            _value = 50m; // Neutral starting point
            return;
        }

        // Calculate gain/loss
        var change = price - _prevPrice;
        var gain = change > 0 ? change : 0m;
        var loss = change < 0 ? -change : 0m;

        _avgGain.Update(gain);
        _avgLoss.Update(loss);

        // Calculate RSI
        if (_avgLoss.Value == 0)
        {
            _value = 100m;
        }
        else
        {
            var rs = _avgGain.Value / _avgLoss.Value;
            _value = 100m - (100m / (1m + rs));
        }

        _prevPrice = price;
    }

    public override void Reset()
    {
        base.Reset();
        _avgGain.Reset();
        _avgLoss.Reset();
        _prevPrice = 0m;
    }
}
