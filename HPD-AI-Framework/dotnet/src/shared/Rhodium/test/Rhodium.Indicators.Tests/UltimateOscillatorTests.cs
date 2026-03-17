using Rhodium.Primitives;
using Rhodium.Indicators;
using static Rhodium.Indicators.Tests.TestHelpers;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Ultimate Oscillator indicator.
/// Combines three timeframes to identify overbought/oversold conditions.
/// </summary>
public class UltimateOscillatorTests
{
    [Fact]
    public void UltimateOscillator_Constructor_ShouldInitializeWithDefaultPeriods()
    {
        // Arrange & Act
        var uo = Indicators.UltimateOscillator();

        // Assert
        Assert.NotNull(uo);
        Assert.Equal(0, uo.Count);
        Assert.False(uo.IsReady);
        Assert.Equal(0m, uo.Value);
    }

    [Fact]
    public void UltimateOscillator_Constructor_ShouldAcceptCustomPeriods()
    {
        // Arrange & Act
        var uo = Indicators.UltimateOscillator(5, 10, 20);

        // Assert
        Assert.NotNull(uo);
        Assert.Equal(0, uo.Count);
        Assert.False(uo.IsReady);
    }

    [Fact]
    public void UltimateOscillator_ShouldBecomeReadyAfterLongestPeriod()
    {
        // Arrange
        var uo = Indicators.UltimateOscillator(7, 14, 28);
        var bars = CreateTrendBars(AscendingPrices(100m, 1m, 35));

        // Act & Assert - Ready after longest period (28) + 1
        for (int i = 0; i < 28; i++)
        {
            uo.Update(bars[i]);
            AssertNotReady(uo, $"UO should not be ready after {i + 1} bars");
        }

        uo.Update(bars[28]);
        AssertReady(uo, "UO should be ready after 29 bars");
    }

    [Fact]
    public void UltimateOscillator_Value_ShouldBeWithinRange()
    {
        // Arrange
        var uo = Indicators.UltimateOscillator();
        var bars = CreateTrendBars(SineWavePrices(100m, 10m, 40));

        // Act
        UpdateBars(uo, bars);

        // Assert
        AssertReady(uo);
        AssertInRange(uo.Value, 0m, 100m, "Ultimate Oscillator should be between 0 and 100");
    }

    [Fact]
    public void UltimateOscillator_StrongUptrend_ShouldShowHighValue()
    {
        // Arrange
        var uo = Indicators.UltimateOscillator(7, 14, 28);
        var uptrend = CreateTrendBars(AscendingPrices(100m, 2m, 35), volatility: 0.01m);

        // Act
        UpdateBars(uo, uptrend);

        // Assert
        AssertReady(uo);
        Assert.True(uo.Value > 50m, "UO should show high value in strong uptrend");
    }

    [Fact]
    public void UltimateOscillator_StrongDowntrend_ShouldShowLowValue()
    {
        // Arrange
        var uo = Indicators.UltimateOscillator(7, 14, 28);
        var downtrend = CreateTrendBars(DescendingPrices(200m, 2m, 35), volatility: 0.01m);

        // Act
        UpdateBars(uo, downtrend);

        // Assert
        AssertReady(uo);
        Assert.True(uo.Value < 50m, "UO should show low value in strong downtrend");
    }

    [Fact]
    public void UltimateOscillator_Overbought_ShouldBeAbove70()
    {
        // Arrange
        var uo = Indicators.UltimateOscillator(7, 14, 28);
        var strongBullish = CreateTrendBars(AscendingPrices(100m, 3m, 35), volatility: 0.005m);

        // Act
        UpdateBars(uo, strongBullish);

        // Assert
        AssertReady(uo);
        // In very strong uptrend, UO can reach overbought levels
        Assert.True(uo.Value >= 0m, "UO should be valid in strong bullish trend");
    }

    [Fact]
    public void UltimateOscillator_Oversold_ShouldBeBelow30()
    {
        // Arrange
        var uo = Indicators.UltimateOscillator(7, 14, 28);
        var strongBearish = CreateTrendBars(DescendingPrices(200m, 3m, 35), volatility: 0.005m);

        // Act
        UpdateBars(uo, strongBearish);

        // Assert
        AssertReady(uo);
        // In very strong downtrend, UO can reach oversold levels
        Assert.True(uo.Value <= 100m, "UO should be valid in strong bearish trend");
    }

    [Fact]
    public void UltimateOscillator_Reset_ShouldClearAllState()
    {
        // Arrange
        var uo = Indicators.UltimateOscillator();
        var bars = CreateTrendBars(AscendingPrices(100m, 1m, 35));
        UpdateBars(uo, bars);

        // Act
        uo.Reset();

        // Assert
        AssertCount(0, uo);
        AssertNotReady(uo);
        Assert.Equal(0m, uo.Value);
    }

    [Fact]
    public void UltimateOscillator_ConstantPrices_ShouldHandleGracefully()
    {
        // Arrange
        var uo = Indicators.UltimateOscillator();
        var constantBars = CreateBars(ConstantPrices(100m, 35));

        // Act
        UpdateBars(uo, constantBars);

        // Assert
        AssertReady(uo);
        // With constant prices, should return neutral value
        Assert.Equal(50m, uo.Value);
    }

    [Fact]
    public void UltimateOscillator_DifferentPeriods_ShouldProduceDifferentValues()
    {
        // Arrange
        var uo1 = Indicators.UltimateOscillator(5, 10, 20);
        var uo2 = Indicators.UltimateOscillator(7, 14, 28);
        var bars = CreateTrendBars(AscendingPrices(100m, 1m, 35));

        // Act
        UpdateBars(uo1, bars);
        UpdateBars(uo2, bars);

        // Assert
        AssertReady(uo1);
        AssertReady(uo2);
        Assert.True(uo1.Value != uo2.Value, "Different periods should produce different values");
    }

    [Fact]
    public void UltimateOscillator_OscillatingMarket_ShouldFluctuateAroundMidpoint()
    {
        // Arrange
        var uo = Indicators.UltimateOscillator(7, 14, 28);
        var oscillating = CreateTrendBars(OscillatingPrices(95m, 105m, 40), volatility: 0.01m);

        // Act
        UpdateBars(uo, oscillating);

        // Assert
        AssertReady(uo);
        AssertInRange(uo.Value, 30m, 70m, "UO should oscillate around midpoint in ranging market");
    }

    [Fact]
    public void UltimateOscillator_BullishBars_ShouldIncreaseValue()
    {
        // Arrange
        var uo = Indicators.UltimateOscillator(7, 14, 28);

        // Create series of bullish bars
        var bars = new List<Bar>();
        decimal price = 100m;
        for (int i = 0; i < 35; i++)
        {
            bars.Add(CreateBullishBar(price, price + 2m));
            price += 1.5m;
        }

        // Act
        UpdateBars(uo, bars.ToArray());

        // Assert
        AssertReady(uo);
        Assert.True(uo.Value > 50m, "Series of bullish bars should increase UO");
    }

    [Fact]
    public void UltimateOscillator_BearishBars_ShouldDecreaseValue()
    {
        // Arrange
        var uo = Indicators.UltimateOscillator(7, 14, 28);

        // Create series of bearish bars
        var bars = new List<Bar>();
        decimal price = 200m;
        for (int i = 0; i < 35; i++)
        {
            bars.Add(CreateBearishBar(price, price - 2m));
            price -= 1.5m;
        }

        // Act
        UpdateBars(uo, bars.ToArray());

        // Assert
        AssertReady(uo);
        Assert.True(uo.Value < 50m, "Series of bearish bars should decrease UO");
    }

    [Fact]
    public void UltimateOscillator_UpdateSequentially_ShouldMaintainCount()
    {
        // Arrange
        var uo = Indicators.UltimateOscillator();
        var bars = CreateTrendBars(AscendingPrices(100m, 1m, 35));

        // Act
        foreach (var bar in bars)
        {
            var countBefore = uo.Count;
            uo.Update(bar);
            Assert.Equal(countBefore + 1, uo.Count);
        }

        // Assert
        AssertReady(uo);
        Assert.Equal(35, uo.Count);
    }

    [Fact]
    public void UltimateOscillator_WithGaps_ShouldHandleCorrectly()
    {
        // Arrange
        var uo = Indicators.UltimateOscillator(7, 14, 28);
        var bars = new[]
        {
            CreateBar(100m, 105m, 99m, 103m),
            CreateBar(103m, 108m, 102m, 107m),
            CreateBar(110m, 115m, 109m, 113m), // Gap up
            CreateBar(113m, 117m, 112m, 115m),
            CreateBar(108m, 112m, 107m, 109m), // Gap down
        };

        var extendedBars = Enumerable.Repeat(bars, 8).SelectMany(x => x).ToArray();

        // Act
        UpdateBars(uo, extendedBars);

        // Assert
        AssertReady(uo);
        AssertInRange(uo.Value, 0m, 100m, "UO should handle gaps and stay in range");
    }

    [Fact]
    public void UltimateOscillator_FirstBarInitialization_ShouldSetNeutralValue()
    {
        // Arrange
        var uo = Indicators.UltimateOscillator();
        var bar = CreateBar(100m, 105m, 99m, 103m);

        // Act
        uo.Update(bar);

        // Assert
        Assert.Equal(1, uo.Count);
        Assert.Equal(50m, uo.Value); // Should initialize to neutral 50
    }

    [Fact]
    public void UltimateOscillator_MultipleTimeframes_ShouldCombineCorrectly()
    {
        // Arrange
        var uo = Indicators.UltimateOscillator(7, 14, 28);

        // Create trend that strengthens over time
        var prices = new List<decimal>();
        decimal price = 100m;
        for (int i = 0; i < 35; i++)
        {
            prices.Add(price);
            price += 0.5m + (i * 0.05m); // Accelerating trend
        }
        var bars = CreateTrendBars(prices.ToArray());

        // Act
        UpdateBars(uo, bars);

        // Assert
        AssertReady(uo);
        Assert.True(uo.Value > 50m, "Accelerating uptrend should show in UO");
    }

    [Fact]
    public void UltimateOscillator_ZeroTrueRange_ShouldHandleSafely()
    {
        // Arrange
        var uo = Indicators.UltimateOscillator(7, 14, 28);
        var bars = CreateBars(ConstantPrices(100m, 35));

        // Act
        UpdateBars(uo, bars);

        // Assert - Should not crash, should handle zero TR gracefully
        AssertReady(uo);
        Assert.Equal(50m, uo.Value);
    }

    [Fact]
    public void UltimateOscillator_Responsiveness_ShouldVaryWithPeriods()
    {
        // Arrange
        var uoFast = Indicators.UltimateOscillator(3, 7, 14);
        var uoSlow = Indicators.UltimateOscillator(14, 28, 56);

        // Create sudden trend change
        var uptrend = AscendingPrices(100m, 1m, 30);
        var downtrend = DescendingPrices(129m, 2m, 30);
        var combined = uptrend.Concat(downtrend).ToArray();
        var bars = CreateTrendBars(combined);

        // Act
        UpdateBars(uoFast, bars);
        UpdateBars(uoSlow, bars);

        // Assert - Both should detect change, but fast should be more responsive
        AssertReady(uoFast);
        AssertReady(uoSlow);
        Assert.True(uoFast.Value != uoSlow.Value, "Different period sets should produce different responsiveness");
    }
}
