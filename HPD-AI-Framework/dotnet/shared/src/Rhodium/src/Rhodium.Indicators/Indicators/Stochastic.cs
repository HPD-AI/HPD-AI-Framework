using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Stochastic %K and %D.
/// O(1) update, zero allocations.
/// </summary>
public sealed class Stochastic : BarIndicatorBase
{
    private readonly int _kPeriod;
    private readonly SMA _dSma;
    private readonly decimal[] _highs;
    private readonly decimal[] _lows;
    private readonly decimal[] _closes;
    private int _index;

    public decimal K { get; private set; }
    public decimal D { get; private set; }

    public override bool IsReady => _count >= _kPeriod && _dSma.IsReady;

    public Stochastic(int kPeriod = 14, int dPeriod = 3)
    {
        if (kPeriod < 1)
            throw new ArgumentException("kPeriod must be >= 1", nameof(kPeriod));

        _kPeriod = kPeriod;
        _dSma = new SMA(dPeriod);
        _highs = new decimal[kPeriod];
        _lows = new decimal[kPeriod];
        _closes = new decimal[kPeriod];
    }

    public override void Update(Bar bar)
    {
        _highs[_index] = bar.High.Value;
        _lows[_index] = bar.Low.Value;
        _closes[_index] = bar.Close.Value;
        _index = (_index + 1) % _kPeriod;
        _count++;

        if (_count >= _kPeriod)
        {
            var highest = decimal.MinValue;
            var lowest = decimal.MaxValue;

            for (int i = 0; i < _kPeriod; i++)
            {
                if (_highs[i] > highest) highest = _highs[i];
                if (_lows[i] < lowest) lowest = _lows[i];
            }

            var close = _closes[(_index - 1 + _kPeriod) % _kPeriod];
            var range = highest - lowest;
            K = range > 0 ? 100m * (close - lowest) / range : 50m;

            _dSma.Update(K);
            D = _dSma.Value;

            _value = K;
        }
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_highs, 0, _highs.Length);
        Array.Clear(_lows, 0, _lows.Length);
        Array.Clear(_closes, 0, _closes.Length);
        _dSma.Reset();
        _index = 0;
        K = 0m;
        D = 0m;
    }
}
