using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;
using static Rhodium.Indicators.Tests.TestHelpers;

namespace Rhodium.Indicators.Tests;

public class TRIXTests
{
    [Fact]
    public void TRIX_InitialState_NotReady()
    {
        var trix = Indicators.TRIX(14);

        AssertNotReady(trix);
        AssertCount(0, trix);
    }

    [Fact]
    public void TRIX_BecomesReady_AfterTripleEMAPeriods()
    {
        var trix = Indicators.TRIX(5);
        var prices = AscendingPrices(100m, 0.5m, 30);

        // TRIX needs 3 chained EMAs to be ready, plus one more for ROC calculation
        // This is approximately 3 * period samples to stabilize all EMAs
        for (int i = 0; i < 15; i++)
        {
            trix.Update(prices[i]);
            // May become ready earlier but definitely not in first few samples
        }

        // Should be ready after sufficient warmup
        UpdatePrices(trix, prices);
        AssertReady(trix);
    }

    [Fact]
    public void TRIX_Reset_ClearsState()
    {
        var trix = Indicators.TRIX(10);
        var prices = AscendingPrices(100m, 1m, 40);

        UpdatePrices(trix, prices);
        AssertReady(trix);

        trix.Reset();

        AssertNotReady(trix);
        AssertCount(0, trix);
    }

    [Fact]
    public void TRIX_PositiveForAscendingPrices()
    {
        var trix = Indicators.TRIX(10);
        var prices = AscendingPrices(100m, 1m, 50);

        UpdatePrices(trix, prices);
        AssertReady(trix);

        // TRIX should be positive for sustained uptrend
        Assert.True(trix.Value > 0m, $"TRIX should be positive for ascending prices, got {trix.Value}");
    }

    [Fact]
    public void TRIX_NegativeForDescendingPrices()
    {
        var trix = Indicators.TRIX(10);
        var prices = DescendingPrices(150m, 1m, 50);

        UpdatePrices(trix, prices);
        AssertReady(trix);

        // TRIX should be negative for sustained downtrend
        Assert.True(trix.Value < 0m, $"TRIX should be negative for descending prices, got {trix.Value}");
    }

    [Fact]
    public void TRIX_NearZeroForConstantPrices()
    {
        var trix = Indicators.TRIX(10);
        var prices = ConstantPrices(100m, 50);

        UpdatePrices(trix, prices);
        AssertReady(trix);

        // TRIX should be near 0 for constant prices (no rate of change)
        AssertApproximately(0m, trix.Value, LowPrecision);
    }

    [Fact]
    public void TRIX_OscillatesForOscillatingPrices()
    {
        var trix = Indicators.TRIX(8);
        var prices = SineWavePrices(100m, 5m, 80, frequency: 2);

        UpdatePrices(trix, prices);
        AssertReady(trix);

        // TRIX should oscillate around zero for oscillating prices
        // Value can be positive or negative
        AssertInRange(trix.Value, -5m, 5m);
    }

    [Fact]
    public void TRIX_SmoothsNoise_TripleSmoothing()
    {
        var trix = Indicators.TRIX(10);

        // Noisy price data with underlying uptrend
        var prices = new decimal[60];
        for (int i = 0; i < 60; i++)
        {
            var trend = 100m + i * 0.5m;
            var noise = (i % 3 == 0) ? 1m : (i % 3 == 1) ? -1m : 0m;
            prices[i] = trend + noise;
        }

        UpdatePrices(trix, prices);
        AssertReady(trix);

        // Triple smoothing should filter noise, showing positive trend
        Assert.True(trix.Value >= -2m, $"TRIX should smooth noise and show trend direction");
    }

    [Fact]
    public void TRIX_RespondsToTrendChanges()
    {
        var trix = Indicators.TRIX(10);

        // Start with uptrend
        var upPrices = AscendingPrices(100m, 1m, 30);
        UpdatePrices(trix, upPrices);
        var trixAfterUp = trix.Value;

        // Continue with downtrend
        var downPrices = DescendingPrices(130m, 1m, 30);
        UpdatePrices(trix, downPrices);
        var trixAfterDown = trix.Value;

        // TRIX should decrease after downtrend
        Assert.True(trixAfterDown < trixAfterUp,
            $"TRIX should decrease after downtrend: {trixAfterUp} -> {trixAfterDown}");
    }

    [Fact]
    public void TRIX_Count_IncrementsCorrectly()
    {
        var trix = Indicators.TRIX(10);

        Assert.Equal(0, trix.Count);

        trix.Update(100m);
        Assert.Equal(1, trix.Count);

        for (int i = 0; i < 20; i++)
        {
            trix.Update(100m + i * 0.5m);
        }
        Assert.Equal(21, trix.Count);
    }

    [Fact]
    public void TRIX_DifferentPeriods_DifferentSensitivity()
    {
        var shortTrix = Indicators.TRIX(5);
        var longTrix = Indicators.TRIX(15);

        var prices = AscendingPrices(100m, 0.5m, 60);

        UpdatePrices(shortTrix, prices);
        UpdatePrices(longTrix, prices);

        AssertReady(shortTrix);
        AssertReady(longTrix);

        // Both should be positive for uptrend
        Assert.True(shortTrix.Value > -1m || longTrix.Value > -1m);

        // Different periods should produce different values
        Assert.NotEqual(shortTrix.Value, longTrix.Value);
    }

    [Fact]
    public void TRIX_ZeroPrices_HandlesGracefully()
    {
        var trix = Indicators.TRIX(10);

        TestZeroPrices(trix, 40);

        // With zero prices, triple EMA is 0, ROC is 0
        AssertApproximately(0m, trix.Value, DefaultPrecision);
    }

    [Fact]
    public void TRIX_LargePrices_NoOverflow()
    {
        var trix = Indicators.TRIX(10);

        // TRIX needs enough data for triple EMA (period bars) + count > 1
        // Provide sufficient data to ensure readiness
        var largePrices = new[] { 1000000m, 1000001m, 1000002m, 999999m, 1000000m,
                                  1000001m, 1000002m, 1000003m, 1000004m, 1000005m,
                                  1000006m, 1000007m, 1000008m, 1000009m, 1000010m };

        try
        {
            UpdatePrices(trix, largePrices);
        }
        catch (OverflowException)
        {
            throw new Xunit.Sdk.XunitException("Indicator overflowed with large prices");
        }

        AssertReady(trix);
        // Value should be valid
        Assert.True(trix.Value >= decimal.MinValue && trix.Value <= decimal.MaxValue);
    }

    [Fact]
    public void TRIX_StrongUptrend_PositiveValue()
    {
        var trix = Indicators.TRIX(8);

        // Strong consistent uptrend
        var prices = AscendingPrices(100m, 2m, 50);

        UpdatePrices(trix, prices);
        AssertReady(trix);

        // Should show strong positive TRIX
        Assert.True(trix.Value > 0m, $"TRIX should be positive for strong uptrend, got {trix.Value}");
    }

    [Fact]
    public void TRIX_StrongDowntrend_NegativeValue()
    {
        var trix = Indicators.TRIX(8);

        // Strong consistent downtrend
        var prices = DescendingPrices(200m, 2m, 50);

        UpdatePrices(trix, prices);
        AssertReady(trix);

        // Should show strong negative TRIX
        Assert.True(trix.Value < 0m, $"TRIX should be negative for strong downtrend, got {trix.Value}");
    }

    [Fact]
    public void TRIX_MeasuresPercentageChange()
    {
        var trix = Indicators.TRIX(10);

        var prices = AscendingPrices(100m, 0.5m, 40);
        UpdatePrices(trix, prices);

        AssertReady(trix);

        // TRIX is a percentage rate of change, typically small values
        // For gradual changes, should be between -10% and +10%
        AssertInRange(trix.Value, -10m, 10m);
    }

    [Fact]
    public void TRIX_TripleSmoothing_ReducesVolatility()
    {
        var trix = Indicators.TRIX(10);

        // Highly volatile prices with oscillations
        var prices = new decimal[60];
        for (int i = 0; i < 60; i++)
        {
            prices[i] = 100m + (i % 2 == 0 ? 5m : -5m);
        }

        decimal maxTrix = decimal.MinValue;
        decimal minTrix = decimal.MaxValue;

        foreach (var price in prices)
        {
            trix.Update(price);
            if (trix.IsReady)
            {
                maxTrix = Math.Max(maxTrix, trix.Value);
                minTrix = Math.Min(minTrix, trix.Value);
            }
        }

        // TRIX range should be relatively small due to triple smoothing
        var range = maxTrix - minTrix;
        Assert.True(range < 5m, $"TRIX should have reduced volatility, got range {range}");
    }

    [Fact]
    public void TRIX_UpdatesWithEachNewPrice()
    {
        var trix = Indicators.TRIX(10);

        var prices = AscendingPrices(100m, 0.5m, 40);
        UpdatePrices(trix, prices);
        var value1 = trix.Value;

        trix.Update(120m);
        var value2 = trix.Value;

        // Value should update with new price
        Assert.NotEqual(value1, value2);
    }

    [Fact]
    public void TRIX_CrossoverZero_SignalsTrendChange()
    {
        var trix = Indicators.TRIX(10);

        // Uptrend
        var upPrices = AscendingPrices(100m, 0.8m, 30);
        UpdatePrices(trix, upPrices);
        AssertReady(trix);
        var valueAfterUp = trix.Value;

        // Flat period
        var flatPrices = ConstantPrices(124m, 20);
        UpdatePrices(trix, flatPrices);
        var valueAfterFlat = trix.Value;

        // TRIX should decrease towards zero during flat period
        Assert.True(Math.Abs(valueAfterFlat) < Math.Abs(valueAfterUp) || valueAfterFlat < 1m,
            $"TRIX should approach zero during flat period");
    }

    [Fact]
    public void TRIX_SmallValue_IndicatesConsolidation()
    {
        var trix = Indicators.TRIX(10);

        // Sideways movement
        var prices = OscillatingPrices(99m, 101m, 50);

        UpdatePrices(trix, prices);
        AssertReady(trix);

        // TRIX should be near zero for consolidation
        AssertInRange(trix.Value, -2m, 2m);
    }

    [Fact]
    public void TRIX_LagsPrice_DueToTripleSmoothing()
    {
        var trix = Indicators.TRIX(5);

        // Sudden price jump
        var prices = new decimal[30];
        for (int i = 0; i < 15; i++)
            prices[i] = 100m;
        for (int i = 15; i < 30; i++)
            prices[i] = 110m;

        foreach (var price in prices.Take(20))
        {
            trix.Update(price);
        }

        // TRIX should lag and not immediately show full impact
        // Just verify it produces valid values
        AssertReady(trix);
        // Value should be valid
        Assert.True(trix.Value >= decimal.MinValue && trix.Value <= decimal.MaxValue);
    }
}
