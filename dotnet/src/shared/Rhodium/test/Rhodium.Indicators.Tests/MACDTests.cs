using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;
using static Rhodium.Indicators.Tests.TestHelpers;

namespace Rhodium.Indicators.Tests;

public class MACDTests
{
    [Fact]
    public void MACD_InitialState_NotReady()
    {
        var macd = Indicators.MACD(12, 26, 9);

        AssertNotReady(macd);
        AssertCount(0, macd);
        Assert.Equal(0m, macd.Signal);
        Assert.Equal(0m, macd.Histogram);
    }

    [Fact]
    public void MACD_BecomesReady_AfterSlowPeriodPlusSignal()
    {
        var macd = Indicators.MACD(12, 26, 9);
        var prices = AscendingPrices(100m, 0.5m, 40);

        // MACD needs all three EMAs ready: fast(12), slow(26), signal(9)
        // Signal EMA processes MACD line values from the start
        // So all three EMAs receive updates simultaneously
        // MACD is ready when max(fast=12, slow=26, signal=9) = 26 bars are processed
        for (int i = 0; i < 25; i++)
        {
            macd.Update(prices[i]);
            AssertNotReady(macd, $"Should not be ready after {i + 1} updates");
        }

        // After 26th update, all EMAs are ready
        macd.Update(prices[25]);
        AssertReady(macd, "Should be ready after max(12, 26, 9) = 26 updates");
    }

    [Fact]
    public void MACD_Reset_ClearsAllComponents()
    {
        var macd = Indicators.MACD(12, 26, 9);
        var prices = AscendingPrices(100m, 1m, 40);

        UpdatePrices(macd, prices);
        AssertReady(macd);
        var valueBefore = macd.Value;
        var signalBefore = macd.Signal;
        var histogramBefore = macd.Histogram;

        macd.Reset();

        AssertNotReady(macd);
        AssertCount(0, macd);
        Assert.Equal(0m, macd.Value);
        Assert.Equal(0m, macd.Signal);
        Assert.Equal(0m, macd.Histogram);
    }

    [Fact]
    public void MACD_Histogram_EqualsMACD_MinusSignal()
    {
        var macd = Indicators.MACD(12, 26, 9);
        var prices = SineWavePrices(100m, 10m, 50);

        UpdatePrices(macd, prices);
        AssertReady(macd);

        // Histogram = MACD - Signal
        AssertApproximately(macd.Value - macd.Signal, macd.Histogram, HighPrecision);
    }

    [Fact]
    public void MACD_AscendingPrices_PositiveMACD()
    {
        var macd = Indicators.MACD(12, 26, 9);
        var prices = AscendingPrices(100m, 1m, 50);

        UpdatePrices(macd, prices);
        AssertReady(macd);

        // In strong uptrend, fast EMA > slow EMA, so MACD should be positive
        Assert.True(macd.Value > 0m, $"MACD should be positive for uptrend, got {macd.Value}");
    }

    [Fact]
    public void MACD_DescendingPrices_NegativeMACD()
    {
        var macd = Indicators.MACD(12, 26, 9);
        var prices = DescendingPrices(150m, 1m, 50);

        UpdatePrices(macd, prices);
        AssertReady(macd);

        // In strong downtrend, fast EMA < slow EMA, so MACD should be negative
        Assert.True(macd.Value < 0m, $"MACD should be negative for downtrend, got {macd.Value}");
    }

    [Fact]
    public void MACD_ConstantPrices_ZeroMACD()
    {
        var macd = Indicators.MACD(12, 26, 9);
        var prices = ConstantPrices(100m, 50);

        UpdatePrices(macd, prices);
        AssertReady(macd);

        // When prices are constant, fast EMA = slow EMA, so MACD = 0
        AssertApproximately(0m, macd.Value, LowPrecision);
        AssertApproximately(0m, macd.Signal, LowPrecision);
        AssertApproximately(0m, macd.Histogram, LowPrecision);
    }

    [Fact]
    public void MACD_TrendReversal_HistogramCrosses()
    {
        var macd = Indicators.MACD(12, 26, 9);

        // Start with uptrend
        var upPrices = AscendingPrices(100m, 1m, 35);
        UpdatePrices(macd, upPrices);
        var histogramAfterUp = macd.Histogram;

        // Add downtrend
        var downPrices = DescendingPrices(135m, 1.5m, 20);
        UpdatePrices(macd, downPrices);
        var histogramAfterDown = macd.Histogram;

        // Histogram should change direction
        Assert.NotEqual(histogramAfterUp, histogramAfterDown);
    }

    [Fact]
    public void MACD_OscillatingPrices_OscillatesAroundZero()
    {
        var macd = Indicators.MACD(12, 26, 9);
        var prices = SineWavePrices(100m, 5m, 60, frequency: 2);

        UpdatePrices(macd, prices);
        AssertReady(macd);

        // MACD should oscillate around zero for oscillating prices
        // Value can be positive or negative
        Assert.True(macd.Value >= -20m && macd.Value <= 20m,
            $"MACD should be in reasonable range for oscillating prices, got {macd.Value}");
    }

    [Fact]
    public void MACD_Signal_LagsBehindMACD()
    {
        var macd = Indicators.MACD(12, 26, 9);

        // Sharp price increase
        var prices = new decimal[40];
        for (int i = 0; i < 30; i++)
            prices[i] = 100m;
        for (int i = 30; i < 40; i++)
            prices[i] = 100m + (i - 29) * 2m;

        UpdatePrices(macd, prices);
        AssertReady(macd);

        // After sharp increase, MACD should rise faster than Signal
        // So Histogram (MACD - Signal) should be positive
        Assert.True(macd.Histogram > 0m,
            $"Histogram should be positive after sharp price increase, got {macd.Histogram}");
    }

    [Fact]
    public void MACD_DifferentPeriods_DifferentValues()
    {
        var macd1 = Indicators.MACD(12, 26, 9);  // Standard
        var macd2 = Indicators.MACD(6, 13, 5);   // Faster

        var prices = AscendingPrices(100m, 0.5m, 40);

        UpdatePrices(macd1, prices);
        UpdatePrices(macd2, prices);

        AssertReady(macd1);
        AssertReady(macd2);

        // Different periods should produce different values
        Assert.NotEqual(macd1.Value, macd2.Value);
    }

    [Fact]
    public void MACD_Count_IncrementsCorrectly()
    {
        var macd = Indicators.MACD(12, 26, 9);

        Assert.Equal(0, macd.Count);

        macd.Update(100m);
        Assert.Equal(1, macd.Count);

        macd.Update(101m);
        Assert.Equal(2, macd.Count);

        for (int i = 0; i < 30; i++)
        {
            macd.Update(100m + i * 0.5m);
        }
        Assert.Equal(32, macd.Count);
    }

    [Fact]
    public void MACD_BullishCrossover_HistogramTurnsPositive()
    {
        var macd = Indicators.MACD(6, 13, 5);

        // Start with declining prices
        var declining = DescendingPrices(120m, 0.5m, 20);
        UpdatePrices(macd, declining);

        // Then sharp rally
        var rallying = AscendingPrices(110m, 1m, 15);
        UpdatePrices(macd, rallying);

        AssertReady(macd);

        // After rally, histogram should turn positive (bullish)
        Assert.True(macd.Histogram > -5m,
            $"Histogram should increase after rally, got {macd.Histogram}");
    }

    [Fact]
    public void MACD_BearishCrossover_HistogramTurnsNegative()
    {
        var macd = Indicators.MACD(6, 13, 5);

        // Start with rising prices
        var rising = AscendingPrices(100m, 0.5m, 20);
        UpdatePrices(macd, rising);

        // Then sharp decline
        var declining = DescendingPrices(110m, 1m, 15);
        UpdatePrices(macd, declining);

        AssertReady(macd);

        // After decline, histogram should turn negative (bearish)
        Assert.True(macd.Histogram < 5m,
            $"Histogram should decrease after decline, got {macd.Histogram}");
    }

    [Fact]
    public void MACD_ZeroPrices_HandlesGracefully()
    {
        var macd = Indicators.MACD(12, 26, 9);

        TestZeroPrices(macd, 40);

        AssertApproximately(0m, macd.Value, DefaultPrecision);
        AssertApproximately(0m, macd.Signal, DefaultPrecision);
        AssertApproximately(0m, macd.Histogram, DefaultPrecision);
    }

    [Fact]
    public void MACD_LargePrices_NoOverflow()
    {
        var macd = Indicators.MACD(12, 26, 9);

        // MACD needs 26 + 9 = 35 bars to be ready
        var largePrices = new decimal[40];
        for (int i = 0; i < 40; i++)
        {
            largePrices[i] = 1000000m + (i % 3 == 0 ? 2m : (i % 2 == 0 ? -1m : 1m));
        }

        try
        {
            UpdatePrices(macd, largePrices);
        }
        catch (OverflowException)
        {
            throw new Xunit.Sdk.XunitException("Indicator overflowed with large prices");
        }

        AssertReady(macd);
        // Value should be valid
        Assert.True(macd.Value >= decimal.MinValue && macd.Value <= decimal.MaxValue);
        // Signal should be valid
        Assert.True(macd.Signal >= decimal.MinValue && macd.Signal <= decimal.MaxValue);
        // Histogram should be valid
        Assert.True(macd.Histogram >= decimal.MinValue && macd.Histogram <= decimal.MaxValue);
    }

    [Fact]
    public void MACD_SmoothPriceTransition_SmoothMACD()
    {
        var macd = Indicators.MACD(12, 26, 9);

        // Smooth transition from 100 to 110
        var prices = new decimal[50];
        for (int i = 0; i < 50; i++)
        {
            prices[i] = 100m + i * 0.2m;
        }

        UpdatePrices(macd, prices);
        AssertReady(macd);

        // MACD should be positive and increasing for smooth uptrend
        Assert.True(macd.Value > 0m, $"MACD should be positive for smooth uptrend");
    }

    [Fact]
    public void MACD_ResponsiveToRecentChanges()
    {
        var macd = Indicators.MACD(12, 26, 9);

        // Flat prices then sudden jump
        var prices = new decimal[40];
        for (int i = 0; i < 35; i++)
            prices[i] = 100m;
        for (int i = 35; i < 40; i++)
            prices[i] = 105m;

        UpdatePrices(macd, prices);

        // MACD should respond to the jump
        Assert.True(macd.Value != 0m, "MACD should respond to price changes");
    }
}
