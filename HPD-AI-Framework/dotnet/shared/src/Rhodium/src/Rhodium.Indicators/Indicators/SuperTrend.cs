using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming SuperTrend indicator.
/// Trend-following indicator based on ATR.
/// O(1) update, uses ATR internally.
/// </summary>
public sealed class SuperTrend : BarIndicatorBase
{
    private readonly int _period;
    private readonly decimal _multiplier;
    private readonly ATR _atr;
    private decimal _basicUpperBand;
    private decimal _basicLowerBand;
    private decimal _trailingUpperBand;
    private decimal _trailingLowerBand;
    private decimal _previousTrailingUpperBand;
    private decimal _previousTrailingLowerBand;
    private decimal _previousClose;
    private decimal _previousSuperTrend;
    private bool _isUpTrend;

    public override bool IsReady => _atr.IsReady;

    public bool IsUpTrend => _isUpTrend;

    public decimal UpperBand => _trailingUpperBand;

    public decimal LowerBand => _trailingLowerBand;

    public SuperTrend(int period = 10, decimal multiplier = 3m)
    {
        if (period < 1)
            throw new ArgumentException("Period must be >= 1", nameof(period));
        if (multiplier <= 0)
            throw new ArgumentException("Multiplier must be > 0", nameof(multiplier));

        _period = period;
        _multiplier = multiplier;
        _atr = new ATR(period);
        _previousSuperTrend = -1m; // Initial state
        _isUpTrend = true;
    }

    public override void Update(Bar bar)
    {
        _atr.Update(bar);
        _count = _atr.Count;

        if (IsReady)
        {
            var currentClose = bar.Close.Value;
            var hl2 = (bar.High.Value + bar.Low.Value) / 2;
            var atr = _atr.Value;

            // Calculate basic bands
            _basicUpperBand = hl2 + _multiplier * atr;
            _basicLowerBand = hl2 - _multiplier * atr;

            // Calculate trailing bands (ratcheting logic)
            _trailingUpperBand = (_basicUpperBand < _previousTrailingUpperBand || _previousClose > _previousTrailingUpperBand)
                ? _basicUpperBand
                : _previousTrailingUpperBand;

            _trailingLowerBand = (_basicLowerBand > _previousTrailingLowerBand || _previousClose < _previousTrailingLowerBand)
                ? _basicLowerBand
                : _previousTrailingLowerBand;

            // Determine SuperTrend value based on state machine
            if (_previousSuperTrend == -1m || _previousSuperTrend == _previousTrailingUpperBand)
            {
                // Initial state or exiting upper trend
                _value = currentClose <= _trailingUpperBand ? _trailingUpperBand : _trailingLowerBand;
            }
            else // _previousSuperTrend == _previousTrailingLowerBand
            {
                // In lower trend
                _value = currentClose >= _trailingLowerBand ? _trailingLowerBand : _trailingUpperBand;
            }

            _isUpTrend = _value == _trailingLowerBand;

            // Update state for next iteration
            _previousClose = currentClose;
            _previousSuperTrend = _value;
            _previousTrailingUpperBand = _trailingUpperBand;
            _previousTrailingLowerBand = _trailingLowerBand;
        }
    }

    public override void Reset()
    {
        base.Reset();
        _atr.Reset();
        _basicUpperBand = 0m;
        _basicLowerBand = 0m;
        _trailingUpperBand = 0m;
        _trailingLowerBand = 0m;
        _previousTrailingUpperBand = 0m;
        _previousTrailingLowerBand = 0m;
        _previousClose = 0m;
        _previousSuperTrend = -1m;
        _isUpTrend = true;
    }
}
