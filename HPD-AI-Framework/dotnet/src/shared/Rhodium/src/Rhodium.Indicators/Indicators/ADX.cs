using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Average Directional Index.
/// O(1) update, measures trend strength.
/// </summary>
public sealed class ADX : BarIndicatorBase
{
    private readonly RMA _smoothedPlusDM;
    private readonly RMA _smoothedMinusDM;
    private readonly RMA _smoothedTR;
    private readonly RMA _adxSmooth;
    private Bar _prevBar;
    private bool _havePrevBar;

    public decimal PlusDI { get; private set; }
    public decimal MinusDI { get; private set; }

    public override bool IsReady => _adxSmooth.IsReady;

    public ADX(int period)
    {
        _smoothedPlusDM = new RMA(period);
        _smoothedMinusDM = new RMA(period);
        _smoothedTR = new RMA(period);
        _adxSmooth = new RMA(period);
    }

    public override void Update(Bar bar)
    {
        _count++;

        if (!_havePrevBar)
        {
            _prevBar = bar;
            _havePrevBar = true;
            return;
        }

        // Calculate directional movement
        var upMove = bar.High.Value - _prevBar.High.Value;
        var dnMove = _prevBar.Low.Value - bar.Low.Value;

        var plusDM = (upMove > dnMove && upMove > 0) ? upMove : 0m;
        var minusDM = (dnMove > upMove && dnMove > 0) ? dnMove : 0m;

        // Calculate true range
        var tr = Math.Max(bar.High.Value - bar.Low.Value,
                 Math.Max(Math.Abs(bar.High.Value - _prevBar.Close.Value),
                          Math.Abs(bar.Low.Value - _prevBar.Close.Value)));

        // Smooth the values
        _smoothedPlusDM.Update(plusDM);
        _smoothedMinusDM.Update(minusDM);
        _smoothedTR.Update(tr);

        // Calculate +DI and -DI
        if (_smoothedTR.Value > 0)
        {
            PlusDI = 100m * _smoothedPlusDM.Value / _smoothedTR.Value;
            MinusDI = 100m * _smoothedMinusDM.Value / _smoothedTR.Value;
        }

        // Calculate DX
        var sum = PlusDI + MinusDI;
        decimal dx = 0m;
        if (sum > 0)
        {
            dx = 100m * Math.Abs(PlusDI - MinusDI) / sum;
        }

        // Smooth DX to get ADX
        _adxSmooth.Update(dx);
        _value = _adxSmooth.Value;

        _prevBar = bar;
    }

    public override void Reset()
    {
        base.Reset();
        _smoothedPlusDM.Reset();
        _smoothedMinusDM.Reset();
        _smoothedTR.Reset();
        _adxSmooth.Reset();
        _havePrevBar = false;
        PlusDI = 0m;
        MinusDI = 0m;
    }
}
