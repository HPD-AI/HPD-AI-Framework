namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Fuzzy Volatility.
/// O(1) update, uses fuzzy logic for volatility classification.
/// Returns membership values for Low, Medium, High states.
/// </summary>
public sealed class FuzzyVolatility : PriceIndicatorBase
{
    private readonly int _period;
    private readonly decimal _lowThreshold;
    private readonly decimal _highThreshold;
    private readonly StdDev _currentStd;
    private readonly decimal[] _stdHistory;
    private int _historyIndex;
    private int _historyCount;
    private decimal _sumStdHistory;

    public decimal Low { get; private set; }
    public decimal Medium { get; private set; }
    public decimal High { get; private set; }

    public override bool IsReady => _currentStd.IsReady && _historyCount >= 10;

    public FuzzyVolatility(int period = 14, decimal lowThreshold = 0.5m, decimal highThreshold = 2m)
    {
        _period = period;
        _lowThreshold = lowThreshold;
        _highThreshold = highThreshold;
        _currentStd = new StdDev(period);
        _stdHistory = new decimal[50];
    }

    public override void Update(decimal price)
    {
        _count++;
        _currentStd.Update(price);

        if (!_currentStd.IsReady)
            return;

        var currentStdValue = _currentStd.Value;

        // Update history
        if (_historyCount < _stdHistory.Length)
        {
            _stdHistory[_historyCount] = currentStdValue;
            _sumStdHistory += currentStdValue;
            _historyCount++;
        }
        else
        {
            var oldValue = _stdHistory[_historyIndex];
            _stdHistory[_historyIndex] = currentStdValue;
            _sumStdHistory = _sumStdHistory - oldValue + currentStdValue;
        }

        _historyIndex = (_historyIndex + 1) % _stdHistory.Length;

        if (_historyCount < 10)
            return;

        var avgStd = _sumStdHistory / _historyCount;
        var normalizedVol = avgStd > 0 ? currentStdValue / avgStd : 1m;

        // Fuzzy membership functions
        var low = FuzzyLow(normalizedVol);
        var medium = FuzzyMedium(normalizedVol);
        var high = FuzzyHigh(normalizedVol);

        // Normalize
        var total = low + medium + high;
        if (total > 0)
        {
            Low = low / total;
            Medium = medium / total;
            High = high / total;
        }

        _value = normalizedVol;
    }

    private decimal FuzzyLow(decimal x) =>
        x <= _lowThreshold ? 1m :
        x >= 1m ? 0m :
        (1m - x) / (1m - _lowThreshold);

    private decimal FuzzyMedium(decimal x) =>
        x <= _lowThreshold ? 0m :
        x <= 1m ? (x - _lowThreshold) / (1m - _lowThreshold) :
        x <= _highThreshold ? (_highThreshold - x) / (_highThreshold - 1m) :
        0m;

    private decimal FuzzyHigh(decimal x) =>
        x <= 1m ? 0m :
        x >= _highThreshold ? 1m :
        (x - 1m) / (_highThreshold - 1m);

    public override void Reset()
    {
        base.Reset();
        _currentStd.Reset();
        Array.Clear(_stdHistory, 0, _stdHistory.Length);
        _historyIndex = 0;
        _historyCount = 0;
        _sumStdHistory = 0m;
        Low = 0m;
        Medium = 0m;
        High = 0m;
    }
}
