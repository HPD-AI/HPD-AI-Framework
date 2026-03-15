using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Ultimate Oscillator.
/// O(1) update using circular buffers for three periods.
/// </summary>
public sealed class UltimateOscillator : BarIndicatorBase
{
    private readonly int _period1;
    private readonly int _period2;
    private readonly int _period3;
    private readonly decimal[] _bpBuffer1;
    private readonly decimal[] _bpBuffer2;
    private readonly decimal[] _bpBuffer3;
    private readonly decimal[] _trBuffer1;
    private readonly decimal[] _trBuffer2;
    private readonly decimal[] _trBuffer3;
    private int _index1, _index2, _index3;
    private decimal _sumBP1, _sumBP2, _sumBP3;
    private decimal _sumTR1, _sumTR2, _sumTR3;
    private Bar _prevBar;
    private bool _havePrevBar;

    public override bool IsReady => _count > _period3;

    public UltimateOscillator(int period1 = 7, int period2 = 14, int period3 = 28)
    {
        _period1 = period1;
        _period2 = period2;
        _period3 = period3;

        _bpBuffer1 = new decimal[period1];
        _bpBuffer2 = new decimal[period2];
        _bpBuffer3 = new decimal[period3];
        _trBuffer1 = new decimal[period1];
        _trBuffer2 = new decimal[period2];
        _trBuffer3 = new decimal[period3];
    }

    public override void Update(Bar bar)
    {
        _count++;

        if (!_havePrevBar)
        {
            _prevBar = bar;
            _havePrevBar = true;
            _value = 50m;
            return;
        }

        // Calculate buying pressure and true range
        var low = Math.Min(bar.Low.Value, _prevBar.Close.Value);
        var bp = bar.Close.Value - low;

        var high = bar.High.Value;
        var barLow = bar.Low.Value;
        var prevClose = _prevBar.Close.Value;
        var tr = Math.Max(high - barLow,
                 Math.Max(Math.Abs(high - prevClose),
                          Math.Abs(barLow - prevClose)));

        // Update period 1
        if (_count > _period1)
        {
            _sumBP1 -= _bpBuffer1[_index1];
            _sumTR1 -= _trBuffer1[_index1];
        }
        _bpBuffer1[_index1] = bp;
        _trBuffer1[_index1] = tr;
        _sumBP1 += bp;
        _sumTR1 += tr;
        _index1 = (_index1 + 1) % _period1;

        // Update period 2
        if (_count > _period2)
        {
            _sumBP2 -= _bpBuffer2[_index2];
            _sumTR2 -= _trBuffer2[_index2];
        }
        _bpBuffer2[_index2] = bp;
        _trBuffer2[_index2] = tr;
        _sumBP2 += bp;
        _sumTR2 += tr;
        _index2 = (_index2 + 1) % _period2;

        // Update period 3
        if (_count > _period3)
        {
            _sumBP3 -= _bpBuffer3[_index3];
            _sumTR3 -= _trBuffer3[_index3];
        }
        _bpBuffer3[_index3] = bp;
        _trBuffer3[_index3] = tr;
        _sumBP3 += bp;
        _sumTR3 += tr;
        _index3 = (_index3 + 1) % _period3;

        // Calculate UO
        if (_sumTR1 == 0 || _sumTR2 == 0 || _sumTR3 == 0)
        {
            _value = 50m;
        }
        else
        {
            var avg1 = _sumBP1 / _sumTR1;
            var avg2 = _sumBP2 / _sumTR2;
            var avg3 = _sumBP3 / _sumTR3;
            _value = 100m * (4m * avg1 + 2m * avg2 + avg3) / 7m;
        }

        _prevBar = bar;
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_bpBuffer1);
        Array.Clear(_bpBuffer2);
        Array.Clear(_bpBuffer3);
        Array.Clear(_trBuffer1);
        Array.Clear(_trBuffer2);
        Array.Clear(_trBuffer3);
        _index1 = _index2 = _index3 = 0;
        _sumBP1 = _sumBP2 = _sumBP3 = 0m;
        _sumTR1 = _sumTR2 = _sumTR3 = 0m;
        _havePrevBar = false;
    }
}
