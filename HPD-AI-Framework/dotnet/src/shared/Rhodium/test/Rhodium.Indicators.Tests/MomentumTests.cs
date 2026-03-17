using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;
using static Rhodium.Indicators.Tests.TestHelpers;

namespace Rhodium.Indicators.Tests;

public class MomentumTests
{
    [Fact]
    public void Momentum_InitialState_NotReady()
    {
        var momentum = Indicators.Momentum(10);

        AssertNotReady(momentum);
        AssertCount(0, momentum);
    }

    [Fact]
    public void Momentum_BecomesReady_AfterPeriodPlusOne()
    {
        var momentum = Indicators.Momentum(10);
        var prices = AscendingPrices(100m, 1m, 15);

        // Momentum needs period+1 prices to calculate first momentum
        // After 10 updates, count=10, which is NOT > period (10)
        for (int i = 0; i < 10; i++)
        {
            momentum.Update(prices[i]);
            AssertNotReady(momentum, $"Should not be ready after {i + 1} updates");
        }

        // After 11th update, count=11, which IS > period (10)
        momentum.Update(prices[10]);
        AssertReady(momentum, "Should be ready after period+1 updates");
    }

    [Fact]
    public void Momentum_Reset_ClearsState()
    {
        var momentum = Indicators.Momentum(10);
        var prices = AscendingPrices(100m, 1m, 15);

        UpdatePrices(momentum, prices);
        AssertReady(momentum);

        momentum.Reset();

        AssertNotReady(momentum);
        AssertCount(0, momentum);
    }

    [Fact]
    public void Momentum_CalculatesAbsoluteChange()
    {
        var momentum = Indicators.Momentum(5);

        // Start at 100, end at 110 after 5 periods
        var prices = Prices(100m, 102m, 104m, 106m, 108m, 110m);

        UpdatePrices(momentum, prices);
        AssertReady(momentum);

        // Momentum = 110 - 100 = 10
        AssertApproximately(10m, momentum.Value, HighPrecision);
    }

    [Fact]
    public void Momentum_PositiveForAscendingPrices()
    {
        var momentum = Indicators.Momentum(10);
        var prices = AscendingPrices(100m, 1m, 20);

        UpdatePrices(momentum, prices);
        AssertReady(momentum);

        // Momentum should be positive for ascending prices
        Assert.True(momentum.Value > 0m, $"Momentum should be positive for ascending prices, got {momentum.Value}");
    }

    [Fact]
    public void Momentum_NegativeForDescendingPrices()
    {
        var momentum = Indicators.Momentum(10);
        var prices = DescendingPrices(100m, 1m, 20);

        UpdatePrices(momentum, prices);
        AssertReady(momentum);

        // Momentum should be negative for descending prices
        Assert.True(momentum.Value < 0m, $"Momentum should be negative for descending prices, got {momentum.Value}");
    }

    [Fact]
    public void Momentum_ZeroForConstantPrices()
    {
        var momentum = Indicators.Momentum(10);
        var prices = ConstantPrices(100m, 20);

        UpdatePrices(momentum, prices);
        AssertReady(momentum);

        // Momentum should be 0 for constant prices
        AssertApproximately(0m, momentum.Value, HighPrecision);
    }

    [Fact]
    public void Momentum_LargeIncrease_LargePositiveMomentum()
    {
        var momentum = Indicators.Momentum(5);

        // 50 point increase
        var prices = Prices(100m, 105m, 110m, 120m, 135m, 150m);

        UpdatePrices(momentum, prices);
        AssertReady(momentum);

        // Momentum = 150 - 100 = 50
        AssertApproximately(50m, momentum.Value, HighPrecision);
    }

    [Fact]
    public void Momentum_LargeDecrease_LargeNegativeMomentum()
    {
        var momentum = Indicators.Momentum(5);

        // 30 point decrease
        var prices = Prices(100m, 95m, 88m, 80m, 75m, 70m);

        UpdatePrices(momentum, prices);
        AssertReady(momentum);

        // Momentum = 70 - 100 = -30
        AssertApproximately(-30m, momentum.Value, HighPrecision);
    }

    [Fact]
    public void Momentum_OscillatingPrices_OscillatesAroundZero()
    {
        var momentum = Indicators.Momentum(10);
        var prices = OscillatingPrices(95m, 105m, 30);

        UpdatePrices(momentum, prices);
        AssertReady(momentum);

        // Momentum should oscillate for oscillating prices
        AssertInRange(momentum.Value, -15m, 15m);
    }

    [Fact]
    public void Momentum_UpdatesWithEachNewPrice()
    {
        var momentum = Indicators.Momentum(5);

        var prices = Prices(100m, 102m, 104m, 106m, 108m, 110m);
        UpdatePrices(momentum, prices);
        var value1 = momentum.Value;
        // value1 = 110 - 100 = 10

        // Update with a different increment to change momentum
        momentum.Update(115m);
        var value2 = momentum.Value;
        // value2 = 115 - 102 = 13

        // Value should change with new price
        Assert.NotEqual(value1, value2);
        Assert.True(value2 > value1, "Momentum should increase with accelerating price");
    }

    [Fact]
    public void Momentum_DifferentPeriods_DifferentValues()
    {
        var shortMomentum = Indicators.Momentum(3);
        var longMomentum = Indicators.Momentum(15);

        var prices = AscendingPrices(100m, 1m, 20);

        UpdatePrices(shortMomentum, prices);
        UpdatePrices(longMomentum, prices);

        AssertReady(shortMomentum);
        AssertReady(longMomentum);

        // Different periods measure different lookback windows
        Assert.NotEqual(shortMomentum.Value, longMomentum.Value);
    }

    [Fact]
    public void Momentum_Count_IncrementsCorrectly()
    {
        var momentum = Indicators.Momentum(10);

        Assert.Equal(0, momentum.Count);

        momentum.Update(100m);
        Assert.Equal(1, momentum.Count);

        for (int i = 0; i < 15; i++)
        {
            momentum.Update(100m + i);
        }
        Assert.Equal(16, momentum.Count);
    }

    [Fact]
    public void Momentum_PeriodOne_ComparesConsecutivePrices()
    {
        var momentum = Indicators.Momentum(1);

        momentum.Update(100m);
        momentum.Update(105m);

        AssertReady(momentum);

        // Momentum = 105 - 100 = 5
        AssertApproximately(5m, momentum.Value, HighPrecision);
    }

    [Fact]
    public void Momentum_SineWave_OscillatesSymmetrically()
    {
        var momentum = Indicators.Momentum(10);
        var prices = SineWavePrices(100m, 10m, 50, frequency: 1);

        decimal minMomentum = decimal.MaxValue;
        decimal maxMomentum = decimal.MinValue;

        foreach (var price in prices)
        {
            momentum.Update(price);
            if (momentum.IsReady)
            {
                minMomentum = Math.Min(minMomentum, momentum.Value);
                maxMomentum = Math.Max(maxMomentum, momentum.Value);
            }
        }

        // Momentum should oscillate with sine wave
        Assert.True(maxMomentum > 0m && minMomentum < 0m, "Momentum should oscillate positive and negative");
    }

    [Fact]
    public void Momentum_ZeroPrices_ReturnsZero()
    {
        var momentum = Indicators.Momentum(5);

        TestZeroPrices(momentum, 15);

        AssertApproximately(0m, momentum.Value, HighPrecision);
    }

    [Fact]
    public void Momentum_LargePrices_NoOverflow()
    {
        var momentum = Indicators.Momentum(10);

        // Momentum needs period+1 prices to be ready
        var largePrices = new[] { 1000000m, 1000001m, 1000002m, 999999m, 1000000m,
                                  1000001m, 1000002m, 1000003m, 1000004m, 1000005m, 1000006m };

        try
        {
            UpdatePrices(momentum, largePrices);
        }
        catch (OverflowException)
        {
            throw new Xunit.Sdk.XunitException("Indicator overflowed with large prices");
        }

        AssertReady(momentum);
        // Value should be valid
        Assert.True(momentum.Value >= decimal.MinValue && momentum.Value <= decimal.MaxValue);
    }

    [Fact]
    public void Momentum_SmallChanges_SmallMomentum()
    {
        var momentum = Indicators.Momentum(5);

        // Very small changes (0.1 per period)
        var prices = Prices(100.00m, 100.02m, 100.04m, 100.06m, 100.08m, 100.10m);

        UpdatePrices(momentum, prices);
        AssertReady(momentum);

        // Momentum should be approximately 0.1
        AssertApproximately(0.1m, momentum.Value, LowPrecision);
    }

    [Fact]
    public void Momentum_ReversalPattern_ShowsDirectionChange()
    {
        var momentum = Indicators.Momentum(5);

        // Up then down
        var prices = Prices(100m, 105m, 110m, 115m, 120m, 125m, 120m, 115m, 110m, 105m, 100m);

        foreach (var price in prices)
        {
            momentum.Update(price);
        }

        AssertReady(momentum);

        // After reversal, momentum should be low or negative
        Assert.True(momentum.Value < 10m, $"Momentum should decrease after reversal, got {momentum.Value}");
    }

    [Fact]
    public void Momentum_LinearRelationshipToROC()
    {
        var momentum = Indicators.Momentum(10);
        var roc = Indicators.ROC(10);

        var prices = AscendingPrices(100m, 1m, 20);

        UpdatePrices(momentum, prices);
        UpdatePrices(roc, prices);

        AssertReady(momentum);
        AssertReady(roc);

        // Both should have same sign (both positive for ascending)
        Assert.True((momentum.Value > 0 && roc.Value > 0) || (momentum.Value < 0 && roc.Value < 0),
            "Momentum and ROC should have same directional bias");
    }

    [Fact]
    public void Momentum_StrongUptrend_LargeMomentum()
    {
        var momentum = Indicators.Momentum(10);

        // Strong uptrend
        var prices = AscendingPrices(100m, 5m, 20);

        UpdatePrices(momentum, prices);
        AssertReady(momentum);

        // Momentum = current - 10 periods ago = (100 + 19*5) - (100 + 9*5) = 50
        Assert.True(momentum.Value > 40m, $"Momentum should be large for strong uptrend, got {momentum.Value}");
    }

    [Fact]
    public void Momentum_StrongDowntrend_LargeNegativeMomentum()
    {
        var momentum = Indicators.Momentum(10);

        // Strong downtrend
        var prices = DescendingPrices(200m, 5m, 20);

        UpdatePrices(momentum, prices);
        AssertReady(momentum);

        // Momentum should be large negative
        Assert.True(momentum.Value < -40m, $"Momentum should be large negative for strong downtrend, got {momentum.Value}");
    }

    [Fact]
    public void Momentum_MeasuresAbsoluteDifference_NotPercentage()
    {
        var momentum = Indicators.Momentum(5);

        var prices1 = Prices(10m, 11m, 12m, 13m, 14m, 15m);  // 5 point increase
        UpdatePrices(momentum, prices1);
        var value1 = momentum.Value;

        momentum.Reset();

        var prices2 = Prices(100m, 101m, 102m, 103m, 104m, 105m);  // 5 point increase
        UpdatePrices(momentum, prices2);
        var value2 = momentum.Value;

        // Both should have same momentum (5) even though percentage change differs
        AssertApproximately(5m, value1, HighPrecision);
        AssertApproximately(5m, value2, HighPrecision);
    }
}
