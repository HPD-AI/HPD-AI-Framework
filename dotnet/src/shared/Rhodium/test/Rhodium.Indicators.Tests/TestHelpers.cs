using Rhodium.Primitives;
using Rhodium.Indicators;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Test helpers and utilities for indicator testing.
/// Provides convenience methods for creating test data and asserting indicator behavior.
/// </summary>
public static class TestHelpers
{
    // ==================== CONSTANTS ====================

    /// <summary>
    /// Default decimal precision for assertions (0.0001).
    /// </summary>
    public const decimal DefaultPrecision = 0.0001m;

    /// <summary>
    /// High precision for strict assertions (0.000001).
    /// </summary>
    public const decimal HighPrecision = 0.000001m;

    /// <summary>
    /// Low precision for loose assertions (0.01).
    /// </summary>
    public const decimal LowPrecision = 0.01m;

    // ==================== PRICE DATA GENERATION ====================

    /// <summary>
    /// Create a simple price sequence for testing.
    /// </summary>
    public static decimal[] Prices(params decimal[] values) => values;

    /// <summary>
    /// Generate ascending price sequence.
    /// </summary>
    public static decimal[] AscendingPrices(decimal start, decimal increment, int count)
    {
        var prices = new decimal[count];
        for (int i = 0; i < count; i++)
        {
            prices[i] = start + (i * increment);
        }
        return prices;
    }

    /// <summary>
    /// Generate descending price sequence.
    /// </summary>
    public static decimal[] DescendingPrices(decimal start, decimal decrement, int count)
    {
        var prices = new decimal[count];
        for (int i = 0; i < count; i++)
        {
            prices[i] = start - (i * decrement);
        }
        return prices;
    }

    /// <summary>
    /// Generate constant price sequence (flat market).
    /// </summary>
    public static decimal[] ConstantPrices(decimal value, int count)
    {
        return Enumerable.Repeat(value, count).ToArray();
    }

    /// <summary>
    /// Generate oscillating prices (up, down, up, down...).
    /// </summary>
    public static decimal[] OscillatingPrices(decimal low, decimal high, int count)
    {
        var prices = new decimal[count];
        for (int i = 0; i < count; i++)
        {
            prices[i] = i % 2 == 0 ? low : high;
        }
        return prices;
    }

    /// <summary>
    /// Generate sine wave prices for smooth oscillation testing.
    /// </summary>
    public static decimal[] SineWavePrices(decimal center, decimal amplitude, int count, double frequency = 1.0)
    {
        var prices = new decimal[count];
        for (int i = 0; i < count; i++)
        {
            var angle = 2 * Math.PI * frequency * i / count;
            prices[i] = center + (decimal)(amplitude * (decimal)Math.Sin(angle));
        }
        return prices;
    }

    // ==================== BAR DATA GENERATION ====================

    /// <summary>
    /// Create a simple bar with specified OHLC values.
    /// </summary>
    public static Bar CreateBar(decimal open, decimal high, decimal low, decimal close, decimal volume = 1000m)
    {
        return new Bar(
            new Price(open),
            new Price(high),
            new Price(low),
            new Price(close),
            new Qty(volume),
            Instant.Now,
            Duration.FromMinutes(1)
        );
    }

    /// <summary>
    /// Create a bar from a close price (OHLC all equal).
    /// </summary>
    public static Bar CreateBar(decimal close, decimal volume = 1000m)
    {
        return CreateBar(close, close, close, close, volume);
    }

    /// <summary>
    /// Create a sequence of bars from close prices.
    /// </summary>
    public static Bar[] CreateBars(params decimal[] closes)
    {
        return closes.Select(c => CreateBar(c)).ToArray();
    }

    /// <summary>
    /// Create a bullish bar (close > open).
    /// </summary>
    public static Bar CreateBullishBar(decimal open, decimal close, decimal? high = null, decimal? low = null, decimal volume = 1000m)
    {
        return CreateBar(
            open,
            high ?? Math.Max(open, close),
            low ?? Math.Min(open, close),
            close,
            volume
        );
    }

    /// <summary>
    /// Create a bearish bar (close < open).
    /// </summary>
    public static Bar CreateBearishBar(decimal open, decimal close, decimal? high = null, decimal? low = null, decimal volume = 1000m)
    {
        return CreateBar(
            open,
            high ?? Math.Max(open, close),
            low ?? Math.Min(open, close),
            close,
            volume
        );
    }

    /// <summary>
    /// Create bars with realistic OHLC relationships for trend testing.
    /// </summary>
    public static Bar[] CreateTrendBars(decimal[] closes, decimal volatility = 0.02m, decimal volume = 1000m)
    {
        var bars = new Bar[closes.Length];
        var rand = new Random(42); // Fixed seed for reproducibility

        for (int i = 0; i < closes.Length; i++)
        {
            var close = closes[i];
            var range = close * volatility;
            var open = close + (decimal)((rand.NextDouble() - 0.5) * 2) * range;
            var high = Math.Max(open, close) + (decimal)(rand.NextDouble()) * range;
            var low = Math.Min(open, close) - (decimal)(rand.NextDouble()) * range;

            bars[i] = CreateBar(open, high, low, close, volume);
        }

        return bars;
    }

    // ==================== INDICATOR UPDATE HELPERS ====================

    /// <summary>
    /// Update a price indicator with multiple values.
    /// </summary>
    public static void UpdatePrices(IPriceIndicator indicator, params decimal[] prices)
    {
        foreach (var price in prices)
        {
            indicator.Update(price);
        }
    }

    /// <summary>
    /// Update a bar indicator with multiple bars.
    /// </summary>
    public static void UpdateBars(IBarIndicator indicator, params Bar[] bars)
    {
        foreach (var bar in bars)
        {
            indicator.Update(bar);
        }
    }

    /// <summary>
    /// Update a bar indicator with close prices (converts to bars).
    /// </summary>
    public static void UpdateWithCloses(IBarIndicator indicator, params decimal[] closes)
    {
        foreach (var close in closes)
        {
            indicator.Update(CreateBar(close));
        }
    }

    // ==================== ASSERTION HELPERS ====================

    /// <summary>
    /// Assert that a decimal value is approximately equal to expected value.
    /// </summary>
    public static void AssertApproximately(decimal expected, decimal actual, decimal precision = DefaultPrecision, string? message = null)
    {
        var diff = Math.Abs(expected - actual);
        var msg = message ?? $"Expected {expected}, but got {actual} (diff: {diff})";

        if (diff > precision)
        {
            throw new Xunit.Sdk.XunitException(msg);
        }
    }

    /// <summary>
    /// Assert that indicator value is approximately equal to expected.
    /// </summary>
    public static void AssertIndicatorValue(decimal expected, IIndicator<decimal> indicator, decimal precision = DefaultPrecision, string? message = null)
    {
        AssertApproximately(expected, indicator.Value, precision, message);
    }

    /// <summary>
    /// Assert that indicator is ready.
    /// </summary>
    public static void AssertReady(IIndicator<decimal> indicator, string? message = null)
    {
        if (!indicator.IsReady)
        {
            throw new Xunit.Sdk.XunitException(message ?? "Expected indicator to be ready");
        }
    }

    /// <summary>
    /// Assert that indicator is not ready.
    /// </summary>
    public static void AssertNotReady(IIndicator<decimal> indicator, string? message = null)
    {
        if (indicator.IsReady)
        {
            throw new Xunit.Sdk.XunitException(message ?? "Expected indicator to not be ready");
        }
    }

    /// <summary>
    /// Assert that indicator count matches expected.
    /// </summary>
    public static void AssertCount(int expected, IIndicator<decimal> indicator, string? message = null)
    {
        if (indicator.Count != expected)
        {
            throw new Xunit.Sdk.XunitException(
                message ?? $"Expected count {expected}, but got {indicator.Count}"
            );
        }
    }

    /// <summary>
    /// Assert that value is within a range.
    /// </summary>
    public static void AssertInRange(decimal value, decimal min, decimal max, string? message = null)
    {
        if (value < min || value > max)
        {
            throw new Xunit.Sdk.XunitException(
                message ?? $"Expected value to be between {min} and {max}, but got {value}"
            );
        }
    }

    // ==================== INDICATOR BEHAVIOR TESTING ====================

    /// <summary>
    /// Test that indicator becomes ready after expected number of updates.
    /// </summary>
    public static void TestReadinessAfterPeriod(IPriceIndicator indicator, int period, decimal[] prices)
    {
        for (int i = 0; i < period - 1; i++)
        {
            indicator.Update(prices[i]);
            AssertNotReady(indicator, $"Indicator should not be ready after {i + 1} updates");
        }

        indicator.Update(prices[period - 1]);
        AssertReady(indicator, $"Indicator should be ready after {period} updates");
    }

    /// <summary>
    /// Test that reset() properly clears indicator state.
    /// </summary>
    public static void TestReset<T>(IIndicator<T> indicator, Action updateAction)
    {
        // Update indicator
        updateAction();

        var countBefore = indicator.Count;
        if (countBefore == 0)
        {
            throw new Xunit.Sdk.XunitException("Indicator was not updated before reset test");
        }

        // Reset
        indicator.Reset();

        // Verify reset
        AssertCount(0, indicator as IIndicator<decimal> ?? throw new InvalidOperationException("Expected decimal indicator"));
        AssertNotReady(indicator as IIndicator<decimal> ?? throw new InvalidOperationException("Expected decimal indicator"));
    }

    /// <summary>
    /// Verify indicator produces different values for different inputs.
    /// </summary>
    public static void TestResponsiveness(IPriceIndicator indicator, decimal[] ascendingPrices, decimal[] descendingPrices)
    {
        // Update with ascending prices
        UpdatePrices(indicator, ascendingPrices);
        var valueAscending = indicator.Value;

        // Reset and update with descending prices
        indicator.Reset();
        UpdatePrices(indicator, descendingPrices);
        var valueDescending = indicator.Value;

        // Values should be different
        if (valueAscending == valueDescending)
        {
            throw new Xunit.Sdk.XunitException(
                $"Indicator produced same value ({valueAscending}) for ascending and descending prices"
            );
        }
    }

    // ==================== SPECIAL VALUE TESTING ====================

    /// <summary>
    /// Test indicator behavior with zero prices.
    /// </summary>
    public static void TestZeroPrices(IPriceIndicator indicator, int count = 10)
    {
        var zeros = ConstantPrices(0m, count);
        UpdatePrices(indicator, zeros);

        // Should not throw, value should be 0 or valid
        // Decimal doesn't have NaN/Infinity, just verify it doesn't throw
    }

    /// <summary>
    /// Test indicator behavior with constant prices.
    /// </summary>
    public static decimal TestConstantPrices(IPriceIndicator indicator, decimal constantValue, int count = 10)
    {
        var prices = ConstantPrices(constantValue, count);
        UpdatePrices(indicator, prices);
        return indicator.Value;
    }

    /// <summary>
    /// Verify indicator handles large price values without overflow.
    /// </summary>
    public static void TestLargePrices(IPriceIndicator indicator)
    {
        var largePrices = new[] { 1000000m, 1000001m, 1000002m, 999999m, 1000000m };

        try
        {
            UpdatePrices(indicator, largePrices);
            // Decimal doesn't have NaN/Infinity, just verify it doesn't throw or overflow
        }
        catch (OverflowException)
        {
            throw new Xunit.Sdk.XunitException("Indicator overflowed with large prices");
        }
    }

    // ==================== COMMON TEST SCENARIOS ====================

    /// <summary>
    /// Run a standard suite of basic tests for any price indicator.
    /// </summary>
    public static void RunBasicPriceIndicatorTests(IPriceIndicator indicator, int expectedPeriod)
    {
        var testPrices = AscendingPrices(100m, 1m, expectedPeriod * 2);

        // Test readiness
        TestReadinessAfterPeriod(indicator, expectedPeriod, testPrices);

        // Test reset
        TestReset(indicator, () => UpdatePrices(indicator, testPrices));

        // Test zero prices
        TestZeroPrices(indicator, expectedPeriod);

        // Test large prices
        indicator.Reset();
        TestLargePrices(indicator);
    }

    /// <summary>
    /// Compare two indicators for similar output given same inputs.
    /// </summary>
    public static void CompareIndicators(
        IPriceIndicator indicator1,
        IPriceIndicator indicator2,
        decimal[] prices,
        decimal precision = DefaultPrecision)
    {
        UpdatePrices(indicator1, prices);
        UpdatePrices(indicator2, prices);

        AssertApproximately(
            indicator1.Value,
            indicator2.Value,
            precision,
            $"Indicators produced different values: {indicator1.Value} vs {indicator2.Value}"
        );
    }

    // ==================== CALCULATION VERIFICATION ====================

    /// <summary>
    /// Calculate Simple Moving Average manually for verification.
    /// </summary>
    public static decimal CalculateSMA(decimal[] prices)
    {
        if (prices.Length == 0) return 0m;
        return prices.Average();
    }

    /// <summary>
    /// Calculate Standard Deviation manually for verification.
    /// </summary>
    public static decimal CalculateStdDev(decimal[] prices)
    {
        if (prices.Length == 0) return 0m;
        var avg = prices.Average();
        var sumSquaredDiff = prices.Sum(p => (p - avg) * (p - avg));
        return (decimal)Math.Sqrt((double)(sumSquaredDiff / prices.Length));
    }

    /// <summary>
    /// Calculate True Range manually for verification.
    /// </summary>
    public static decimal CalculateTrueRange(Bar current, Bar? previous = null)
    {
        if (previous == null)
        {
            return current.Range.Value;
        }

        var tr1 = current.High.Value - current.Low.Value;
        var tr2 = Math.Abs(current.High.Value - previous.Value.Close.Value);
        var tr3 = Math.Abs(current.Low.Value - previous.Value.Close.Value);

        return Math.Max(tr1, Math.Max(tr2, tr3));
    }
}
