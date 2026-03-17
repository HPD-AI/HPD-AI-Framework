using Rhodium.Indicators.Streaming;

namespace Rhodium.Indicators;

/// <summary>
/// Factory for creating high-performance streaming indicators.
/// All indicators provide O(1) update complexity and zero allocations per update.
/// Optimized for HFT and real-time trading scenarios.
/// Usage: var rsi = Indicators.RSI(14);
/// </summary>
public static class Indicators
{
    // ==================== BASIC MOVING AVERAGES ====================

    /// <summary>
    /// Create Simple Moving Average.
    /// </summary>
    public static SMA SMA(int period) => new(period);

    /// <summary>
    /// Create Exponential Moving Average.
    /// </summary>
    public static EMA EMA(int period) => new(period);

    /// <summary>
    /// Create Wilder's Moving Average (RMA).
    /// </summary>
    public static RMA RMA(int period) => new(period);

    /// <summary>
    /// Create Weighted Moving Average.
    /// </summary>
    public static WMA WMA(int period) => new(period);

    /// <summary>
    /// Create Linear Weighted Moving Average (alias for WMA).
    /// </summary>
    public static WMA LWMA(int period) => new(period);

    /// <summary>
    /// Create Standard Deviation indicator.
    /// </summary>
    public static StdDev StdDev(int period) => new(period);

    // ==================== STATISTICAL INDICATORS ====================

    /// <summary>
    /// Create Z-Score indicator.
    /// </summary>
    public static ZScore ZScore(int period) => new(period);

    /// <summary>
    /// Create Linear Regression value indicator.
    /// </summary>
    public static LinearReg LinearReg(int period) => new(period);

    /// <summary>
    /// Create Linear Regression Slope indicator.
    /// </summary>
    public static LinearRegSlope LinearRegSlope(int period) => new(period);

    /// <summary>
    /// Create Maximum value indicator.
    /// </summary>
    public static Max Max(int period) => new(period);

    /// <summary>
    /// Create Minimum value indicator.
    /// </summary>
    public static Min Min(int period) => new(period);

    /// <summary>
    /// Create Sum indicator.
    /// </summary>
    public static Sum Sum(int period) => new(period);

    /// <summary>
    /// Create Efficiency Ratio (Kaufman's ER) indicator.
    /// </summary>
    public static EfficiencyRatio EfficiencyRatio(int period) => new(period);

    // ==================== ADVANCED MOVING AVERAGES ====================

    /// <summary>
    /// Create Double Exponential Moving Average.
    /// </summary>
    public static DEMA DEMA(int period) => new(period);

    /// <summary>
    /// Create Triple Exponential Moving Average.
    /// </summary>
    public static TEMA TEMA(int period) => new(period);

    /// <summary>
    /// Create Hull Moving Average.
    /// </summary>
    public static HMA HMA(int period) => new(period);

    /// <summary>
    /// Create Kaufman Adaptive Moving Average.
    /// </summary>
    public static KAMA KAMA(int period, int fast = 2, int slow = 30) => new(period, fast, slow);

    /// <summary>
    /// Create Triangular Moving Average.
    /// </summary>
    public static TRIMA TRIMA(int period) => new(period);

    /// <summary>
    /// Create Zero-Lag Exponential Moving Average.
    /// </summary>
    public static ZLEMA ZLEMA(int period) => new(period);

    /// <summary>
    /// Create Variable Index Dynamic Average.
    /// </summary>
    public static VIDYA VIDYA(int period, int cmoPeriod = 9) => new(period, cmoPeriod);

    // ==================== MOMENTUM INDICATORS ====================

    /// <summary>
    /// Create Relative Strength Index.
    /// </summary>
    public static RSI RSI(int period) => new(period);

    /// <summary>
    /// Create MACD (Moving Average Convergence Divergence).
    /// </summary>
    public static MACD MACD(int fast = 12, int slow = 26, int signal = 9) => new(fast, slow, signal);

    /// <summary>
    /// Create Rate of Change.
    /// </summary>
    public static ROC ROC(int period) => new(period);

    /// <summary>
    /// Create Momentum indicator.
    /// </summary>
    public static Momentum Momentum(int period) => new(period);

    /// <summary>
    /// Create TRIX (Triple EMA ROC).
    /// </summary>
    public static TRIX TRIX(int period) => new(period);

    /// <summary>
    /// Create Chande Momentum Oscillator.
    /// </summary>
    public static CMO CMO(int period) => new(period);

    /// <summary>
    /// Create Psychological Line.
    /// </summary>
    public static PsychologicalLine PsychologicalLine(int period) => new(period);

    /// <summary>
    /// Create Percentage Price Oscillator.
    /// </summary>
    public static PPO PPO(int fast = 12, int slow = 26) => new(fast, slow);

    /// <summary>
    /// Create Detrended Price Oscillator.
    /// </summary>
    public static DPO DPO(int period) => new(period);

    /// <summary>
    /// Create Bias indicator.
    /// </summary>
    public static Bias Bias(int period) => new(period);

    /// <summary>
    /// Create Vertical Horizontal Filter.
    /// </summary>
    public static VHF VHF(int period) => new(period);

    /// <summary>
    /// Create Advanced Moving Average Trend.
    /// </summary>
    public static AMAT AMAT(int fast = 8, int medium = 21, int slow = 55) => new(fast, medium, slow);

    // ==================== BAR-BASED MOMENTUM ====================

    /// <summary>
    /// Create Williams %R.
    /// </summary>
    public static WilliamsR WilliamsR(int period) => new(period);

    /// <summary>
    /// Create Commodity Channel Index.
    /// </summary>
    public static CCI CCI(int period, decimal constant = 0.015m) => new(period, constant);

    /// <summary>
    /// Create Stochastic oscillator (%K and %D).
    /// </summary>
    public static Stochastic Stochastic(int kPeriod = 14, int dPeriod = 3) => new(kPeriod, dPeriod);

    // ==================== VOLATILITY INDICATORS ====================

    /// <summary>
    /// Create Bollinger Bands.
    /// </summary>
    public static BollingerBands BollingerBands(int period = 20, decimal multiplier = 2m) => new(period, multiplier);

    /// <summary>
    /// Create Average True Range.
    /// </summary>
    public static ATR ATR(int period) => new(period);

    /// <summary>
    /// Create Volatility Ratio.
    /// </summary>
    public static VolatilityRatio VolatilityRatio(int period) => new(period);

    /// <summary>
    /// Create Relative Volatility Index.
    /// </summary>
    public static RVI RVI(int period = 14, int stdPeriod = 10) => new(period, stdPeriod);

    /// <summary>
    /// Create Fuzzy Volatility indicator.
    /// </summary>
    public static FuzzyVolatility FuzzyVolatility(int period = 14, decimal lowThreshold = 0.5m, decimal highThreshold = 2m)
        => new(period, lowThreshold, highThreshold);

    // ==================== VOLUME INDICATORS ====================

    /// <summary>
    /// Create Volume Weighted Average Price.
    /// </summary>
    public static VWAP VWAP() => new();

    /// <summary>
    /// Create On Balance Volume.
    /// </summary>
    public static OBV OBV() => new();

    /// <summary>
    /// Create Money Flow Index.
    /// </summary>
    public static MFI MFI(int period) => new(period);

    /// <summary>
    /// Create Accumulation/Distribution.
    /// </summary>
    public static AD AD() => new();

    /// <summary>
    /// Create Chaikin Money Flow.
    /// </summary>
    public static CMF CMF(int period) => new(period);

    /// <summary>
    /// Create Klinger Volume Oscillator.
    /// </summary>
    public static KlingerOscillator KlingerOscillator(int fast = 34, int slow = 55, int signal = 13)
        => new(fast, slow, signal);

    /// <summary>
    /// Create Buy/Sell Pressure indicator.
    /// </summary>
    public static Pressure Pressure(int period) => new(period);

    // ==================== DIRECTIONAL INDICATORS ====================

    /// <summary>
    /// Create Average Directional Index.
    /// Includes PlusDI and MinusDI properties.
    /// </summary>
    public static ADX ADX(int period) => new(period);

    // ==================== OSCILLATORS ====================

    /// <summary>
    /// Create Ultimate Oscillator.
    /// </summary>
    public static UltimateOscillator UltimateOscillator(int p1 = 7, int p2 = 14, int p3 = 28)
        => new(p1, p2, p3);

    // ==================== PRICE ACTION ====================

    /// <summary>
    /// Create Parabolic SAR.
    /// Tracks trend reversals with IsLong property.
    /// </summary>
    public static PSAR PSAR(decimal afStart = 0.02m, decimal afIncrement = 0.02m, decimal afMax = 0.2m)
        => new(afStart, afIncrement, afMax);

    /// <summary>
    /// Create Aroon Up and Down indicator.
    /// </summary>
    public static Aroon Aroon(int period) => new(period);

    /// <summary>
    /// Create Aroon Oscillator (Aroon Up - Aroon Down).
    /// </summary>
    public static AroonOsc AroonOsc(int period) => new(period);

    /// <summary>
    /// Create SuperTrend indicator.
    /// </summary>
    public static SuperTrend SuperTrend(int period = 10, decimal multiplier = 3m) => new(period, multiplier);

    /// <summary>
    /// Create Ichimoku Cloud indicator.
    /// </summary>
    public static Ichimoku Ichimoku(int tenkanPeriod = 9, int kijunPeriod = 26, int senkouBPeriod = 52)
        => new(tenkanPeriod, kijunPeriod, senkouBPeriod);

    /// <summary>
    /// Create Swing High detector.
    /// </summary>
    public static SwingHigh SwingHigh(int leftBars = 2, int rightBars = 2) => new(leftBars, rightBars);

    /// <summary>
    /// Create Swing Low detector.
    /// </summary>
    public static SwingLow SwingLow(int leftBars = 2, int rightBars = 2) => new(leftBars, rightBars);

    /// <summary>
    /// Create Pivot Points.
    /// </summary>
    public static PivotPoints PivotPoints() => new();

    /// <summary>
    /// Create Donchian Channel.
    /// </summary>
    public static DonchianChannel DonchianChannel(int period) => new(period);

    /// <summary>
    /// Create Keltner Channel.
    /// </summary>
    public static KeltnerChannel KeltnerChannel(int period, decimal multiplier = 2m) => new(period, multiplier);

    /// <summary>
    /// Create Keltner Position.
    /// </summary>
    public static KeltnerPosition KeltnerPosition(int period, decimal multiplier = 2m) => new(period, multiplier);
}
