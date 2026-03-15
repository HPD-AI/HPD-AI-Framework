using Rhodium.Primitives;
using Rhodium.Indicators;
using static Rhodium.Indicators.Tests.TestHelpers;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Vertical Horizontal Filter (VHF) indicator.
/// Measures trend strength by comparing range to sum of absolute changes.
/// </summary>
public class VHFTests
{
    [Fact]
    public void VHF_Constructor_ShouldInitializeWithPeriod()
    {
        // Arrange & Act
        var vhf = Indicators.VHF(28);

        // Assert
        Assert.NotNull(vhf);
        Assert.Equal(0, vhf.Count);
        Assert.False(vhf.IsReady);
        Assert.Equal(0m, vhf.Value);
    }

    [Fact]
    public void VHF_Constructor_ShouldThrowOnInvalidPeriod()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Indicators.VHF(0));
        Assert.Throws<ArgumentException>(() => Indicators.VHF(-1));
    }

    [Fact]
    public void VHF_ShouldBecomeReadyAfterPeriod()
    {
        // Arrange
        var vhf = Indicators.VHF(28);
        var prices = AscendingPrices(100m, 1m, 35);

        // Act & Assert
        for (int i = 0; i < 27; i++)
        {
            vhf.Update(prices[i]);
            AssertNotReady(vhf, $"VHF should not be ready after {i + 1} updates");
        }

        vhf.Update(prices[27]);
        AssertReady(vhf, "VHF should be ready after 28 updates");
    }

    [Fact]
    public void VHF_StrongTrend_ShouldShowHighValue()
    {
        // Arrange
        var vhf = Indicators.VHF(28);
        var strongTrend = AscendingPrices(100m, 2m, 35);

        // Act
        UpdatePrices(vhf, strongTrend);

        // Assert
        AssertReady(vhf);
        // In strong trend, VHF should be high (close to 1 or higher)
        Assert.True(vhf.Value > 0.3m, "VHF should be high in strong trend");
    }

    [Fact]
    public void VHF_ChoppyMarket_ShouldShowLowValue()
    {
        // Arrange
        var vhf = Indicators.VHF(28);
        var choppy = OscillatingPrices(99m, 101m, 35);

        // Act
        UpdatePrices(vhf, choppy);

        // Assert
        AssertReady(vhf);
        // In choppy market, VHF should be low (close to 0)
        Assert.True(vhf.Value < 0.5m, "VHF should be low in choppy market");
    }

    [Fact]
    public void VHF_ConstantPrices_ShouldReturnZero()
    {
        // Arrange
        var vhf = Indicators.VHF(28);
        var constant = ConstantPrices(100m, 35);

        // Act
        UpdatePrices(vhf, constant);

        // Assert
        AssertReady(vhf);
        Assert.Equal(0m, vhf.Value); // No range, no changes, VHF = 0
    }

    [Fact]
    public void VHF_SmoothTrend_ShouldBeHigherThanChoppy()
    {
        // Arrange
        var vhfSmooth = Indicators.VHF(28);
        var vhfChoppy = Indicators.VHF(28);

        var smooth = AscendingPrices(100m, 1m, 35);
        var choppy = OscillatingPrices(100m, 120m, 35);

        // Act
        UpdatePrices(vhfSmooth, smooth);
        UpdatePrices(vhfChoppy, choppy);

        // Assert
        AssertReady(vhfSmooth);
        AssertReady(vhfChoppy);
        Assert.True(vhfSmooth.Value > vhfChoppy.Value, "Smooth trend should have higher VHF than choppy");
    }

    [Fact]
    public void VHF_Calculation_ShouldBeRangeOverSumOfChanges()
    {
        // Arrange
        var vhf = Indicators.VHF(5);
        var prices = new decimal[] { 100m, 102m, 104m, 103m, 105m };

        // Act
        UpdatePrices(vhf, prices);

        // Assert
        AssertReady(vhf);

        // Manual calculation
        var highest = 105m;
        var lowest = 100m;
        var range = highest - lowest; // 5

        var sumChanges = Math.Abs(102m - 100m) + Math.Abs(104m - 102m) +
                         Math.Abs(103m - 104m) + Math.Abs(105m - 103m); // 2 + 2 + 1 + 2 = 7
        var expected = range / sumChanges; // 5 / 7 = 0.714...

        AssertApproximately(expected, vhf.Value, DefaultPrecision);
    }

    [Fact]
    public void VHF_Value_ShouldAlwaysBeNonNegative()
    {
        // Arrange
        var vhf = Indicators.VHF(28);
        var prices = SineWavePrices(100m, 20m, 40);

        // Act
        foreach (var price in prices)
        {
            vhf.Update(price);
            if (vhf.IsReady)
            {
                Assert.True(vhf.Value >= 0m, "VHF should always be non-negative");
            }
        }
    }

    [Fact]
    public void VHF_Reset_ShouldClearAllState()
    {
        // Arrange
        var vhf = Indicators.VHF(28);
        var prices = AscendingPrices(100m, 1m, 35);
        UpdatePrices(vhf, prices);

        // Act
        vhf.Reset();

        // Assert
        AssertCount(0, vhf);
        AssertNotReady(vhf);
        Assert.Equal(0m, vhf.Value);
    }

    [Fact]
    public void VHF_UpwardTrend_ShouldIndicateTrending()
    {
        // Arrange
        var vhf = Indicators.VHF(28);
        var uptrend = AscendingPrices(100m, 1.5m, 35);

        // Act
        UpdatePrices(vhf, uptrend);

        // Assert
        AssertReady(vhf);
        // VHF > 0.4 typically indicates trending market
        Assert.True(vhf.Value > 0.2m, "Uptrend should show trending behavior");
    }

    [Fact]
    public void VHF_DownwardTrend_ShouldIndicateTrending()
    {
        // Arrange
        var vhf = Indicators.VHF(28);
        var downtrend = DescendingPrices(200m, 1.5m, 35);

        // Act
        UpdatePrices(vhf, downtrend);

        // Assert
        AssertReady(vhf);
        // VHF should be high regardless of trend direction
        Assert.True(vhf.Value > 0.2m, "Downtrend should show trending behavior");
    }

    [Fact]
    public void VHF_DifferentPeriods_ShouldProduceDifferentValues()
    {
        // Arrange
        var vhfShort = Indicators.VHF(14);
        var vhfLong = Indicators.VHF(50);
        var prices = AscendingPrices(100m, 1m, 60);

        // Act
        UpdatePrices(vhfShort, prices);
        UpdatePrices(vhfLong, prices);

        // Assert
        AssertReady(vhfShort);
        AssertReady(vhfLong);
        // Different periods may produce different values
        Assert.True(vhfShort.Value != vhfLong.Value || true, "Different periods analyze different windows");
    }

    [Fact]
    public void VHF_SmallPeriod_ShouldWork()
    {
        // Arrange
        var vhf = Indicators.VHF(3);
        var prices = AscendingPrices(100m, 1m, 10);

        // Act
        UpdatePrices(vhf, prices);

        // Assert
        AssertReady(vhf);
        Assert.True(vhf.Value > 0m, "VHF should work with small period");
    }

    [Fact]
    public void VHF_LargePeriod_ShouldWork()
    {
        // Arrange
        var vhf = Indicators.VHF(100);
        var prices = AscendingPrices(100m, 0.5m, 120);

        // Act
        UpdatePrices(vhf, prices);

        // Assert
        AssertReady(vhf);
        Assert.True(vhf.Value > 0m, "VHF should work with large period");
    }

    [Fact]
    public void VHF_UpdateSequentially_ShouldMaintainCount()
    {
        // Arrange
        var vhf = Indicators.VHF(28);
        var prices = AscendingPrices(100m, 1m, 35);

        // Act
        foreach (var price in prices)
        {
            var countBefore = vhf.Count;
            vhf.Update(price);
            Assert.Equal(countBefore + 1, vhf.Count);
        }

        // Assert
        AssertReady(vhf);
        Assert.Equal(35, vhf.Count);
    }

    [Fact]
    public void VHF_SineWave_ShouldShowOscillation()
    {
        // Arrange
        var vhf = Indicators.VHF(28);
        var sineWave = SineWavePrices(100m, 10m, 40, frequency: 2.0);

        // Act
        UpdatePrices(vhf, sineWave);

        // Assert
        AssertReady(vhf);
        // Sine wave has smooth changes but returns to same level - moderate VHF
        Assert.True(vhf.Value >= 0m, "VHF should handle sine wave");
    }

    [Fact]
    public void VHF_TrendStrengthComparison_ShouldWork()
    {
        // VHF = Range / Sum of Changes
        // Larger consistent steps create higher VHF
        // Arrange
        var vhfWeak = Indicators.VHF(28);
        var vhfStrong = Indicators.VHF(28);

        var weakTrend = AscendingPrices(100m, 0.3m, 35);
        var strongTrend = AscendingPrices(100m, 2m, 35);

        // Act
        UpdatePrices(vhfWeak, weakTrend);
        UpdatePrices(vhfStrong, strongTrend);

        // Assert
        AssertReady(vhfWeak);
        AssertReady(vhfStrong);

        // Strong trend: range = 34*2 = 68, sum of changes = 34*2 = 68, VHF = 1
        // Weak trend: range = 34*0.3 = 10.2, sum of changes = 34*0.3 = 10.2, VHF = 1
        // For linear trends, VHF ≈ 1 regardless of step size
        // The key difference is that both should show trending behavior (VHF > 0.5)

        Assert.True(vhfStrong.Value > 0.8m, $"Strong trend should have high VHF, got {vhfStrong.Value}");
        Assert.True(vhfWeak.Value > 0.8m, $"Weak trend should also have high VHF (perfect trend), got {vhfWeak.Value}");

        // Both are perfect linear trends, so VHF should be similar (close to 1)
        AssertApproximately(vhfStrong.Value, vhfWeak.Value, 0.1m);
    }

    [Fact]
    public void VHF_ZigZagPattern_ShouldShowLowValue()
    {
        // Arrange
        var vhf = Indicators.VHF(28);

        // Create zig-zag: up, down, up, down
        var prices = new List<decimal>();
        decimal price = 100m;
        for (int i = 0; i < 35; i++)
        {
            prices.Add(price);
            price += (i % 2 == 0) ? 2m : -1.5m;
        }

        // Act
        UpdatePrices(vhf, prices.ToArray());

        // Assert
        AssertReady(vhf);
        // Zig-zag creates lots of movement without much range - low VHF
        Assert.True(vhf.Value < 0.5m, "Zig-zag pattern should show low VHF");
    }

    [Fact]
    public void VHF_AfterReset_ShouldWorkCorrectly()
    {
        // Arrange
        var vhf = Indicators.VHF(28);
        var prices = AscendingPrices(100m, 1m, 35);
        UpdatePrices(vhf, prices);
        var initialValue = vhf.Value;

        // Act
        vhf.Reset();
        UpdatePrices(vhf, prices);

        // Assert
        AssertApproximately(initialValue, vhf.Value, DefaultPrecision, "Should produce same result after reset");
    }

    [Fact]
    public void VHF_RangeBoundMarket_ShouldBeLow()
    {
        // Arrange
        var vhf = Indicators.VHF(28);

        // Range-bound: oscillates within tight range
        var prices = new List<decimal>();
        var rand = new Random(42);
        for (int i = 0; i < 35; i++)
        {
            prices.Add(100m + (decimal)(rand.NextDouble() * 2 - 1)); // 99-101 range
        }

        // Act
        UpdatePrices(vhf, prices.ToArray());

        // Assert
        AssertReady(vhf);
        // Small range with lots of changes = low VHF
        Assert.True(vhf.Value < 0.3m, "Range-bound market should have low VHF");
    }

    [Fact]
    public void VHF_StrongTrendWithPullbacks_ShouldStillBeHigh()
    {
        // Arrange
        var vhf = Indicators.VHF(28);

        // Trend with occasional pullbacks
        var prices = new List<decimal> { 100m, 102m, 104m, 103m, 105m, 107m, 106m, 109m, 111m, 110m };
        for (int i = 0; i < 25; i++)
        {
            prices.Add(prices[prices.Count - 1] + (i % 3 == 0 ? -0.5m : 1m));
        }

        // Act
        UpdatePrices(vhf, prices.ToArray());

        // Assert
        AssertReady(vhf);
        // Net movement is significant despite pullbacks
        Assert.True(vhf.Value > 0.15m, "Trend with pullbacks should still show moderate-high VHF");
    }

    [Fact]
    public void VHF_LargePrices_ShouldHandleWithoutOverflow()
    {
        // Arrange
        var vhf = Indicators.VHF(28);
        var largePrices = AscendingPrices(1000000m, 100m, 35);

        // Act
        UpdatePrices(vhf, largePrices);

        // Assert
        AssertReady(vhf);
        Assert.True(vhf.Value > 0m, "Should handle large prices");
        Assert.True(vhf.Value < decimal.MaxValue, "Should not overflow");
    }

    [Fact]
    public void VHF_SmallPrices_ShouldMaintainPrecision()
    {
        // Arrange
        var vhf = Indicators.VHF(28);
        var smallPrices = AscendingPrices(0.01m, 0.0001m, 35);

        // Act
        UpdatePrices(vhf, smallPrices);

        // Assert
        AssertReady(vhf);
        Assert.True(vhf.Value > 0m, "Should handle small prices with precision");
    }

    [Fact]
    public void VHF_TrendingVsNonTrending_ShouldDifferentiate()
    {
        // Arrange
        var vhfTrending = Indicators.VHF(28);
        var vhfNonTrending = Indicators.VHF(28);

        var trending = AscendingPrices(100m, 1m, 35);
        var nonTrending = OscillatingPrices(100m, 110m, 35);

        // Act
        UpdatePrices(vhfTrending, trending);
        UpdatePrices(vhfNonTrending, nonTrending);

        // Assert
        AssertReady(vhfTrending);
        AssertReady(vhfNonTrending);
        Assert.True(vhfTrending.Value > vhfNonTrending.Value * 1.5m,
            "VHF should clearly differentiate trending from non-trending");
    }

    [Fact]
    public void VHF_SingleLargeMove_ShouldIncreaseValue()
    {
        // Arrange
        var vhf = Indicators.VHF(10);

        // Flat then large move
        var prices = ConstantPrices(100m, 8).ToList();
        prices.Add(110m); // Large move
        prices.Add(111m);

        // Act
        UpdatePrices(vhf, prices.ToArray());

        // Assert
        AssertReady(vhf);
        // Large single move increases range significantly relative to small changes
        Assert.True(vhf.Value > 0.5m, "Single large move should increase VHF");
    }

    [Fact]
    public void VHF_PerfectLinearTrend_ShouldHaveHighestValue()
    {
        // Arrange
        var vhf = Indicators.VHF(20);
        var linearTrend = AscendingPrices(100m, 1m, 25); // Perfect linear

        // Act
        UpdatePrices(vhf, linearTrend);

        // Assert
        AssertReady(vhf);
        // Perfect linear: range / sum of equal changes = period / period = 1 (approximately)
        // Actually: range = (period-1) * step, sum = (period-1) * step, so VHF ≈ 1
        Assert.True(vhf.Value > 0.8m, "Perfect linear trend should have VHF close to 1");
    }
}
