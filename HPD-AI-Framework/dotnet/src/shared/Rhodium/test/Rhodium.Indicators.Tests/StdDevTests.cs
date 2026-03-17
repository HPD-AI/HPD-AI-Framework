using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

public class StdDevTests
{
    [Fact]
    public void BasicFunctionality_CalculatesStandardDeviation()
    {
        var std = Indicators.StdDev(5);
        var prices = TestHelpers.Prices(100m, 102m, 104m, 103m, 101m);

        TestHelpers.UpdatePrices(std, prices);

        TestHelpers.AssertReady(std);

        var expectedStdDev = TestHelpers.CalculateStdDev(prices);
        TestHelpers.AssertApproximately(expectedStdDev, std.Value, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void BecomesReadyAfterPeriod()
    {
        var std = Indicators.StdDev(20);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 25);

        TestHelpers.TestReadinessAfterPeriod(std, 20, prices);
    }

    [Fact]
    public void ResetClearsState()
    {
        var std = Indicators.StdDev(10);

        TestHelpers.TestReset(std, () =>
        {
            var prices = TestHelpers.AscendingPrices(100m, 1m, 15);
            TestHelpers.UpdatePrices(std, prices);
        });
    }

    [Fact]
    public void ConstantPricesProduceZeroStdDev()
    {
        var std = Indicators.StdDev(10);
        var prices = TestHelpers.ConstantPrices(100m, 15);

        TestHelpers.UpdatePrices(std, prices);

        TestHelpers.AssertReady(std);
        TestHelpers.AssertApproximately(0m, std.Value, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void OscillatingPricesIncreaseStdDev()
    {
        var std1 = Indicators.StdDev(10);
        var std2 = Indicators.StdDev(10);

        // Low variation
        var lowVar = TestHelpers.AscendingPrices(100m, 0.1m, 15);
        TestHelpers.UpdatePrices(std1, lowVar);

        // High variation
        var highVar = TestHelpers.OscillatingPrices(90m, 110m, 15);
        TestHelpers.UpdatePrices(std2, highVar);

        TestHelpers.AssertReady(std1);
        TestHelpers.AssertReady(std2);

        Assert.True(std2.Value > std1.Value);
    }

    [Fact]
    public void AlwaysNonNegative()
    {
        var std = Indicators.StdDev(10);
        var prices = TestHelpers.DescendingPrices(200m, 2m, 20);

        TestHelpers.UpdatePrices(std, prices);

        TestHelpers.AssertReady(std);
        Assert.True(std.Value >= 0m);
    }

    [Fact]
    public void ManualCalculationVerification()
    {
        var std = Indicators.StdDev(5);

        // Known values for verification
        var prices = TestHelpers.Prices(10m, 12m, 23m, 23m, 16m);
        TestHelpers.UpdatePrices(std, prices);

        // Manual calculation:
        // Mean = (10 + 12 + 23 + 23 + 16) / 5 = 84 / 5 = 16.8
        // Variance = ((10-16.8)^2 + (12-16.8)^2 + (23-16.8)^2 + (23-16.8)^2 + (16-16.8)^2) / 5
        //          = (46.24 + 23.04 + 38.44 + 38.44 + 0.64) / 5
        //          = 146.8 / 5 = 29.36
        // StdDev = sqrt(29.36) ≈ 5.418

        var expected = TestHelpers.CalculateStdDev(prices);
        TestHelpers.AssertApproximately(expected, std.Value, 0.01m);
    }

    [Fact]
    public void RollingWindowBehavior()
    {
        var std = Indicators.StdDev(3);

        std.Update(10m);
        std.Update(10m);
        std.Update(10m);

        // Window: [10, 10, 10] -> StdDev = 0
        TestHelpers.AssertApproximately(0m, std.Value, TestHelpers.DefaultPrecision);

        std.Update(20m);

        // Window: [10, 10, 20] -> StdDev > 0
        Assert.True(std.Value > 0m);

        std.Update(20m);

        // Window: [10, 20, 20] -> StdDev still > 0
        Assert.True(std.Value > 0m);

        std.Update(20m);

        // Window: [20, 20, 20] -> StdDev = 0
        TestHelpers.AssertApproximately(0m, std.Value, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void LargePriceValues()
    {
        var std = Indicators.StdDev(10);

        // TestLargePrices uses prices around 1 billion - should handle without overflow
        var largePrices = new decimal[15];
        for (int i = 0; i < 15; i++)
        {
            largePrices[i] = 1_000_000_000m + i * 100_000m;
        }

        TestHelpers.UpdatePrices(std, largePrices);

        TestHelpers.AssertReady(std);
        Assert.True(std.Value >= 0m, "StdDev should be non-negative");
        Assert.True(std.Value > 0m, "StdDev should be positive for varying prices");
    }

    [Fact]
    public void ZeroPrices()
    {
        var std = Indicators.StdDev(10);
        TestHelpers.TestZeroPrices(std, 15);

        TestHelpers.AssertReady(std);
        TestHelpers.AssertApproximately(0m, std.Value, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void DifferentPeriods()
    {
        var std5 = Indicators.StdDev(5);
        var std20 = Indicators.StdDev(20);

        var prices = TestHelpers.SineWavePrices(100m, 10m, 30);

        TestHelpers.UpdatePrices(std5, prices);
        TestHelpers.UpdatePrices(std20, prices);

        TestHelpers.AssertReady(std5);
        TestHelpers.AssertReady(std20);

        Assert.True(std5.Value >= 0m);
        Assert.True(std20.Value >= 0m);
    }

    [Fact]
    public void SingleValuePeriod()
    {
        var std = Indicators.StdDev(1);

        std.Update(100m);

        TestHelpers.AssertReady(std);

        // Single value has zero deviation
        TestHelpers.AssertApproximately(0m, std.Value, TestHelpers.DefaultPrecision);

        std.Update(110m);

        // Still zero deviation (only one value in window)
        TestHelpers.AssertApproximately(0m, std.Value, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void SineWavePricesProduceConsistentStdDev()
    {
        var std = Indicators.StdDev(20);
        var prices = TestHelpers.SineWavePrices(100m, 10m, 50, 1.0);

        TestHelpers.UpdatePrices(std, prices);

        TestHelpers.AssertReady(std);

        // Sine wave should produce relatively stable std dev
        Assert.True(std.Value > 0m);
        Assert.True(std.Value < 15m); // Should be less than amplitude
    }

    [Fact]
    public void Responsiveness()
    {
        // StdDev measures volatility, not trend direction.
        // Ascending and descending prices with the same step size have similar volatility patterns.
        // This test verifies that StdDev responds to changes in volatility patterns.

        var std = Indicators.StdDev(10);

        // Low volatility - constant prices
        var lowVol = TestHelpers.ConstantPrices(100m, 15);
        TestHelpers.UpdatePrices(std, lowVol);
        var lowVolValue = std.Value;

        std.Reset();

        // High volatility - oscillating prices
        var highVol = TestHelpers.OscillatingPrices(90m, 110m, 15);
        TestHelpers.UpdatePrices(std, highVol);
        var highVolValue = std.Value;

        // Assert - High volatility should produce higher StdDev
        Assert.True(highVolValue > lowVolValue,
            $"High volatility should have higher StdDev. Low: {lowVolValue}, High: {highVolValue}");
    }
}
