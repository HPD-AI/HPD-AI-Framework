using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Swing High detector.
/// Returns true if the pivot bar is a swing high (higher than surrounding bars).
/// O(1) update using circular buffer.
/// </summary>
public sealed class SwingHigh : BarIndicatorBase
{
    private readonly int _leftBars;
    private readonly int _rightBars;
    private readonly int _totalBars;
    private readonly decimal[] _highs;
    private int _index;
    private bool _swingDetected;

    public override bool IsReady => _count >= _totalBars;

    public decimal High { get; private set; }

    public bool IsSwing => _swingDetected;

    public SwingHigh(int leftBars = 2, int rightBars = 2)
    {
        if (leftBars < 1)
            throw new ArgumentException("Left bars must be >= 1", nameof(leftBars));
        if (rightBars < 1)
            throw new ArgumentException("Right bars must be >= 1", nameof(rightBars));

        _leftBars = leftBars;
        _rightBars = rightBars;
        _totalBars = leftBars + rightBars + 1;
        _highs = new decimal[_totalBars];
    }

    public override void Update(Bar bar)
    {
        _highs[_index] = bar.High.Value;
        _index = (_index + 1) % _totalBars;
        _count++;

        if (IsReady)
        {
            // Pivot is at index = leftBars positions back
            var pivotIdx = (_index + _totalBars - _rightBars - 1) % _totalBars;
            var pivotHigh = _highs[pivotIdx];

            _swingDetected = true;

            // Check left bars
            for (int i = 0; i < _leftBars; i++)
            {
                var idx = (_index + i) % _totalBars;
                if (_highs[idx] >= pivotHigh)
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
                    if (_highs[idx] >= pivotHigh)
                    {
                        _swingDetected = false;
                        break;
                    }
                }
            }

            High = pivotHigh;
            _value = _swingDetected ? 1m : 0m;
        }
    }

    public override void Reset()
    {
        base.Reset();
        _index = 0;
        _swingDetected = false;
        High = 0m;
        Array.Clear(_highs);
    }
}
