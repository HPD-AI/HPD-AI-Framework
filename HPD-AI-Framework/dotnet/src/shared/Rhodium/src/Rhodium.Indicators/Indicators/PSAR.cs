using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Parabolic SAR.
/// O(1) update, tracks trend reversals.
/// </summary>
public sealed class PSAR : BarIndicatorBase
{
    private readonly decimal _afStart;
    private readonly decimal _afIncrement;
    private readonly decimal _afMax;

    private bool _isLong;
    private decimal _sar;
    private decimal _ep;  // Extreme point
    private decimal _af;  // Acceleration factor
    private Bar _prevBar;
    private Bar _prevPrevBar;
    private bool _initialized;

    public bool IsLong => _isLong;
    public decimal EP => _ep;
    public decimal AF => _af;

    public override bool IsReady => _initialized && _count >= 2;

    public PSAR(decimal afStart = 0.02m, decimal afIncrement = 0.02m, decimal afMax = 0.2m)
    {
        _afStart = afStart;
        _afIncrement = afIncrement;
        _afMax = afMax;
    }

    public override void Update(Bar bar)
    {
        _count++;

        if (_count == 1)
        {
            _prevBar = bar;
            _value = bar.Close.Value;
            return;
        }

        if (_count == 2)
        {
            // Initialize based on first two bars
            _isLong = bar.Close.Value > _prevBar.Close.Value;
            _sar = _isLong ? _prevBar.Low.Value : _prevBar.High.Value;
            _ep = _isLong ? bar.High.Value : bar.Low.Value;
            _af = _afStart;
            _initialized = true;
            _prevPrevBar = _prevBar;
            _prevBar = bar;
            _value = Math.Abs(_sar);
            return;
        }

        // Calculate new SAR
        var newSAR = _sar + _af * (_ep - _sar);
        var reversed = false;

        if (_isLong)
        {
            // Ensure SAR is below last two lows
            newSAR = Math.Min(newSAR, Math.Min(_prevBar.Low.Value, _prevPrevBar.Low.Value));

            // Check for reversal to short
            if (bar.Low.Value < newSAR)
            {
                _isLong = false;
                _sar = _ep;  // SAR becomes previous EP
                _ep = bar.Low.Value;
                _af = _afStart;
                reversed = true;
            }
        }
        else
        {
            // Ensure SAR is above last two highs
            newSAR = Math.Max(newSAR, Math.Max(_prevBar.High.Value, _prevPrevBar.High.Value));

            // Check for reversal to long
            if (bar.High.Value > newSAR)
            {
                _isLong = true;
                _sar = _ep;  // SAR becomes previous EP
                _ep = bar.High.Value;
                _af = _afStart;
                reversed = true;
            }
        }

        if (!reversed)
        {
            _sar = newSAR;

            // Update EP and AF
            if (_isLong && bar.High.Value > _ep)
            {
                _ep = bar.High.Value;
                _af = Math.Min(_af + _afIncrement, _afMax);
            }
            else if (!_isLong && bar.Low.Value < _ep)
            {
                _ep = bar.Low.Value;
                _af = Math.Min(_af + _afIncrement, _afMax);
            }
        }

        _prevPrevBar = _prevBar;
        _prevBar = bar;
        _value = Math.Abs(_sar);
    }

    public override void Reset()
    {
        base.Reset();
        _isLong = false;
        _sar = 0m;
        _ep = 0m;
        _af = _afStart;
        _initialized = false;
    }
}
