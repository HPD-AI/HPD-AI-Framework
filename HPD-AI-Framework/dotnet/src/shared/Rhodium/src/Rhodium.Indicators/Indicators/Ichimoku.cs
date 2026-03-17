using Rhodium.Primitives;

namespace Rhodium.Indicators.Streaming;

/// <summary>
/// Streaming Ichimoku Cloud indicator.
/// Complex multi-component trend indicator.
/// O(1) update using circular buffers.
/// </summary>
public sealed class Ichimoku : BarIndicatorBase
{
    private readonly int _tenkanPeriod;
    private readonly int _kijunPeriod;
    private readonly int _senkouBPeriod;
    private readonly decimal[] _highs;
    private readonly decimal[] _lows;
    private readonly decimal[] _closes;
    private int _index;

    public override bool IsReady => _count >= _senkouBPeriod;

    public decimal Tenkan { get; private set; }
    public decimal Kijun { get; private set; }
    public decimal SenkouA { get; private set; }
    public decimal SenkouB { get; private set; }
    public decimal Chikou { get; private set; }

    public Ichimoku(int tenkanPeriod = 9, int kijunPeriod = 26, int senkouBPeriod = 52)
    {
        if (tenkanPeriod < 1)
            throw new ArgumentException("Tenkan period must be >= 1", nameof(tenkanPeriod));
        if (kijunPeriod < tenkanPeriod)
            throw new ArgumentException("Kijun period must be >= Tenkan period", nameof(kijunPeriod));
        if (senkouBPeriod < kijunPeriod)
            throw new ArgumentException("Senkou B period must be >= Kijun period", nameof(senkouBPeriod));

        _tenkanPeriod = tenkanPeriod;
        _kijunPeriod = kijunPeriod;
        _senkouBPeriod = senkouBPeriod;

        // Use largest period for buffer size
        _highs = new decimal[senkouBPeriod];
        _lows = new decimal[senkouBPeriod];
        _closes = new decimal[senkouBPeriod];
    }

    public override void Update(Bar bar)
    {
        _highs[_index] = bar.High.Value;
        _lows[_index] = bar.Low.Value;
        _closes[_index] = bar.Close.Value;
        _index = (_index + 1) % _senkouBPeriod;
        _count++;

        if (IsReady)
        {
            Tenkan = CalculateMidpoint(_tenkanPeriod);
            Kijun = CalculateMidpoint(_kijunPeriod);
            SenkouA = (Tenkan + Kijun) / 2;
            SenkouB = CalculateMidpoint(_senkouBPeriod);
            Chikou = bar.Close.Value;

            // Primary value is Tenkan
            _value = Tenkan;
        }
    }

    private decimal CalculateMidpoint(int period)
    {
        var high = decimal.MinValue;
        var low = decimal.MaxValue;

        var count = Math.Min(period, _count);
        for (int i = 0; i < count; i++)
        {
            var idx = (_index + _senkouBPeriod - count + i) % _senkouBPeriod;
            if (_highs[idx] > high) high = _highs[idx];
            if (_lows[idx] < low) low = _lows[idx];
        }

        return (high + low) / 2;
    }

    public override void Reset()
    {
        base.Reset();
        _index = 0;
        Tenkan = 0m;
        Kijun = 0m;
        SenkouA = 0m;
        SenkouB = 0m;
        Chikou = 0m;
        Array.Clear(_highs);
        Array.Clear(_lows);
        Array.Clear(_closes);
    }
}
