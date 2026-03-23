using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

public class FuzzyVolatilityTests
{
    [Fact]
    public void BasicFunctionality_CalculatesFuzzyMemberships()
    {
        var fv = Indicators.FuzzyVolatility(14, 0.5m, 2m);
        var prices = TestHelpers.OscillatingPrices(95m, 105m, 30);

        TestHelpers.UpdatePrices(fv, prices);

        TestHelpers.AssertReady(fv);

        // Memberships should sum to approximately 1 (normalized)
        var sum = fv.Low + fv.Medium + fv.High;
        TestHelpers.AssertApproximately(1m, sum, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void BecomesReadyAfterHistoryBuilt()
    {
        var fv = Indicators.FuzzyVolatility(14, 0.5m, 2m);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 30);

        // FuzzyVolatility needs:
        // 1. StdDev to be ready (14 updates for period 14)
        // 2. Then historyCount >= 10
        // Since history starts building when StdDev is ready, after 14 updates:
        //   - Update 14: StdDev becomes ready, history[0] added, count = 1
        //   - Update 15-23: history[1-9] added, count = 2-10
        // So it becomes ready after 14 + 9 = 23 updates

        for (int i = 0; i < 22; i++)
        {
            fv.Update(prices[i]);
            TestHelpers.AssertNotReady(fv, $"Should not be ready after {i + 1} updates");
        }

        fv.Update(prices[22]);
        TestHelpers.AssertReady(fv, "Should be ready after 23 updates (14 for StdDev + 9 more for history count=10)");
    }

    [Fact]
    public void ResetClearsState()
    {
        var fv = Indicators.FuzzyVolatility(14, 0.5m, 2m);

        TestHelpers.TestReset(fv, () =>
        {
            var prices = TestHelpers.OscillatingPrices(90m, 110m, 30);
            TestHelpers.UpdatePrices(fv, prices);
        });

        Assert.Equal(0m, fv.Low);
        Assert.Equal(0m, fv.Medium);
        Assert.Equal(0m, fv.High);
    }

    [Fact]
    public void MembershipsSumToOne()
    {
        var fv = Indicators.FuzzyVolatility(14, 0.5m, 2m);
        var prices = TestHelpers.SineWavePrices(100m, 10m, 30);

        TestHelpers.UpdatePrices(fv, prices);

        TestHelpers.AssertReady(fv);

        var sum = fv.Low + fv.Medium + fv.High;
        TestHelpers.AssertApproximately(1m, sum, 0.01m);
    }

    [Fact]
    public void LowVolatilityProducesHighLowMembership()
    {
        var fv = Indicators.FuzzyVolatility(10, 0.5m, 2m);

        // Start with some variation to build history
        var prices = TestHelpers.OscillatingPrices(99m, 101m, 20);
        TestHelpers.UpdatePrices(fv, prices);

        // Then add constant prices (very low volatility)
        var constantPrices = TestHelpers.ConstantPrices(100m, 10);
        TestHelpers.UpdatePrices(fv, constantPrices);

        TestHelpers.AssertReady(fv);

        // Low membership should be highest for low volatility
        Assert.True(fv.Low >= fv.Medium);
        Assert.True(fv.Low >= fv.High);
    }

    [Fact]
    public void HighVolatilityProducesHighHighMembership()
    {
        var fv = Indicators.FuzzyVolatility(10, 0.5m, 2m);

        // Start with low volatility to establish baseline
        var prices = TestHelpers.ConstantPrices(100m, 15);
        TestHelpers.UpdatePrices(fv, prices);

        // Then add high volatility
        var highVolPrices = TestHelpers.OscillatingPrices(80m, 120m, 15);
        TestHelpers.UpdatePrices(fv, highVolPrices);

        TestHelpers.AssertReady(fv);

        // High membership should be elevated
        Assert.True(fv.High >= fv.Low);
    }

    [Fact]
    public void MediumVolatilityProducesHighMediumMembership()
    {
        var fv = Indicators.FuzzyVolatility(14, 0.5m, 2m);

        // Create moderately varying prices around baseline
        var prices = TestHelpers.SineWavePrices(100m, 3m, 40, 2.0);

        TestHelpers.UpdatePrices(fv, prices);

        TestHelpers.AssertReady(fv);

        // For moderate volatility, medium membership should be significant
        Assert.True(fv.Medium > 0.1m);
    }

    [Fact]
    public void ValueRepresentsNormalizedVolatility()
    {
        var fv = Indicators.FuzzyVolatility(14, 0.5m, 2m);
        var prices = TestHelpers.OscillatingPrices(90m, 110m, 30);

        TestHelpers.UpdatePrices(fv, prices);

        TestHelpers.AssertReady(fv);

        // Value should be positive (normalized volatility)
        Assert.True(fv.Value > 0);
    }

    [Fact]
    public void DifferentThresholds()
    {
        var fv1 = Indicators.FuzzyVolatility(14, 0.3m, 1.5m);  // Tighter thresholds
        var fv2 = Indicators.FuzzyVolatility(14, 0.7m, 3m);    // Wider thresholds

        var prices = TestHelpers.OscillatingPrices(95m, 105m, 30);

        TestHelpers.UpdatePrices(fv1, prices);
        TestHelpers.UpdatePrices(fv2, prices);

        TestHelpers.AssertReady(fv1);
        TestHelpers.AssertReady(fv2);

        // Different thresholds should produce different membership distributions
        // but both should sum to 1
        var sum1 = fv1.Low + fv1.Medium + fv1.High;
        var sum2 = fv2.Low + fv2.Medium + fv2.High;

        TestHelpers.AssertApproximately(1m, sum1, 0.01m);
        TestHelpers.AssertApproximately(1m, sum2, 0.01m);
    }

    [Fact]
    public void AllMembershipsNonNegative()
    {
        var fv = Indicators.FuzzyVolatility(14, 0.5m, 2m);
        var prices = TestHelpers.DescendingPrices(200m, 2m, 30);

        TestHelpers.UpdatePrices(fv, prices);

        TestHelpers.AssertReady(fv);

        Assert.True(fv.Low >= 0m);
        Assert.True(fv.Medium >= 0m);
        Assert.True(fv.High >= 0m);
    }

    [Fact]
    public void OscillatingPricesIncreaseVolatility()
    {
        var fv1 = Indicators.FuzzyVolatility(14, 0.5m, 2m);
        var fv2 = Indicators.FuzzyVolatility(14, 0.5m, 2m);

        // FuzzyVolatility normalizes current StdDev against historical average
        // Need sufficient history (at least 50+ to fill history buffer) for meaningful comparison

        // Start with oscillating prices, then transition to constant to establish history
        var initial1 = TestHelpers.OscillatingPrices(95m, 105m, 30);
        TestHelpers.UpdatePrices(fv1, initial1);

        // Then add constant prices (should show low volatility vs history)
        var constantPrices = TestHelpers.ConstantPrices(100m, 30);
        TestHelpers.UpdatePrices(fv1, constantPrices);

        // Start with constant, then oscillating
        var initial2 = TestHelpers.ConstantPrices(100m, 30);
        TestHelpers.UpdatePrices(fv2, initial2);

        // Then add oscillating prices (should show high volatility vs history)
        var oscPrices = TestHelpers.OscillatingPrices(85m, 115m, 30);
        TestHelpers.UpdatePrices(fv2, oscPrices);

        TestHelpers.AssertReady(fv1);
        TestHelpers.AssertReady(fv2);

        // fv2 should have higher current volatility relative to its history
        Assert.True(fv2.Value > fv1.Value,
            $"Oscillating prices should have higher normalized volatility. Constant-end: {fv1.Value}, Oscillating-end: {fv2.Value}");
    }

    [Fact]
    public void HandleZeroPrices()
    {
        var fv = Indicators.FuzzyVolatility(14, 0.5m, 2m);
        TestHelpers.TestZeroPrices(fv, 30);

        if (fv.IsReady)
        {
            var sum = fv.Low + fv.Medium + fv.High;
            TestHelpers.AssertApproximately(1m, sum, 0.01m);
        }
    }
}
