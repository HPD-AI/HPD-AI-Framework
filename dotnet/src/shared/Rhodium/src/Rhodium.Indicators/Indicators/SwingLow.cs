using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Swing Low detector.
/// Returns true if the pivot bar is a swing low (lower than surrounding bars).
/// O(1) update using circular buffer.
/// </summary>
public sealed class SwingLow : BarIndicatorBase
{
    private readonly int _leftBars;
    private readonly int _rightBars;
    private readonly int _totalBars;
    private readonly decimal[] _lows;
    private int _index;
    private bool _swingDetected;

    public override bool IsReady => _count >= _totalBars;

    public decimal Low { get; private set; }

    public bool IsSwing => _swingDetected;

    public SwingLow(int leftBars = 2, int rightBars = 2)
    {
        if (leftBars < 1)
            throw new ArgumentException("Left bars must be >= 1", nameof(leftBars));
        if (rightBars < 1)
            throw new ArgumentException("Right bars must be >= 1", nameof(rightBars));

        _leftBars = leftBars;
        _rightBars = rightBars;
        _totalBars = leftBars + rightBars + 1;
        _lows = new decimal[_totalBars];
    }

    public override void Update(Bar bar)
    {
        _lows[_index] = bar.Low.Value;
        _index = (_index + 1) % _totalBars;
        _count++;

        if (IsReady)
        {
            // Pivot is at index = leftBars positions back
            var pivotIdx = (_index + _totalBars - _rightBars - 1) % _totalBars;
            var pivotLow = _lows[pivotIdx];

            _swingDetected = true;

            // Check left bars
            for (int i = 0; i < _leftBars; i++)
            {
                var idx = (_index + i) % _totalBars;
                if (_lows[idx] <= pivotLow)
                {
                    _swingDetected = false;
                    break;
                }
            }

            // Check right bars
            if (_swingDetected)
            {
                for (int i = _leftBars + 1; i < _totalBars; i++)
                {
                    var idx = (_index + i) % _totalBars;
                    if (_lows[idx] <= pivotLow)
                    {
                        _swingDetected = false;
                        break;
                    }
                }
            }

            Low = pivotLow;
            _value = _swingDetected ? 1m : 0m;
        }
    }

    public override void Reset()
    {
        base.Reset();
        _index = 0;
        _swingDetected = false;
        Low = 0m;
        Array.Clear(_lows);
    }
}
