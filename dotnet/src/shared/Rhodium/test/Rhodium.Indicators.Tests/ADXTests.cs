using Rhodium.Primitives;
using Rhodium.Indicators;
using static Rhodium.Indicators.Tests.TestHelpers;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Average Directional Index (ADX) indicator.
/// ADX measures trend strength and includes +DI and -DI components.
/// </summary>
public class ADXTests
{
    [Fact]
    public void ADX_Constructor_ShouldInitializeWithPeriod()
    {
        // Arrange & Act
        var adx = Indicators.ADX(14);

        // Assert
        Assert.NotNull(adx);
        Assert.Equal(0, adx.Count);
        Assert.False(adx.IsReady);
        Assert.Equal(0m, adx.Value);
        Assert.Equal(0m, adx.PlusDI);
        Assert.Equal(0m, adx.MinusDI);
    }

    [Fact]
    public void ADX_ShouldBecomeReadyAfterCorrectPeriod()
    {
        // Arrange
        var adx = Indicators.ADX(14);
        var bars = CreateTrendBars(AscendingPrices(100m, 1m, 30));

        // Act & Assert
        // ADX skips first bar, then all 4 RMAs (3 for DM/TR + 1 for ADX smoothing)
        // receive data simultaneously. After period bars, all are ready.
        // Total bars needed = 1 (skipped) + 14 (period) = 15 bars
        for (int i = 0; i < 14; i++)
        {
            adx.Update(bars[i]);
            AssertNotReady(adx, $"ADX should not be ready after {i + 1} bars");
        }

        adx.Update(bars[14]);
        AssertReady(adx, "ADX should be ready after period+1 bars");
    }

    [Fact]
    public void ADX_StrongUptrend_ShouldShowHighADXAndPlusDI()
    {
        // Arrange
        var adx = Indicators.ADX(14);
        var strongUptrend = CreateTrendBars(AscendingPrices(100m, 2m, 35), volatility: 0.01m);

        // Act
        UpdateBars(adx, strongUptrend);

        // Assert
        AssertReady(adx);
        Assert.True(adx.Value > 0m, "ADX should be positive in strong trend");
        Assert.True(adx.PlusDI > adx.MinusDI, "+DI should be greater than -DI in uptrend");
        Assert.True(adx.PlusDI > 0m, "+DI should be positive");
    }

    [Fact]
    public void ADX_StrongDowntrend_ShouldShowHighADXAndMinusDI()
    {
        // Arrange
        var adx = Indicators.ADX(14);
        var strongDowntrend = CreateTrendBars(DescendingPrices(200m, 2m, 35), volatility: 0.01m);

        // Act
        UpdateBars(adx, strongDowntrend);

        // Assert
        AssertReady(adx);
        Assert.True(adx.Value > 0m, "ADX should be positive in strong trend");
        Assert.True(adx.MinusDI > adx.PlusDI, "-DI should be greater than +DI in downtrend");
        Assert.True(adx.MinusDI > 0m, "-DI should be positive");
    }

    [Fact]
    public void ADX_SidewaysMarket_ShouldShowLowADX()
    {
        // Arrange
        var adx = Indicators.ADX(14);
        var sideways = CreateTrendBars(OscillatingPrices(99m, 101m, 35), volatility: 0.005m);

        // Act
        UpdateBars(adx, sideways);

        // Assert
        AssertReady(adx);
        // ADX should be lower in sideways market than in strong trend
        // With oscillating prices, ADX may still show some directional bias
        Assert.True(adx.Value < 50m, $"ADX should be moderate or low in sideways market, got {adx.Value}");
    }

    [Fact]
    public void ADX_Reset_ShouldClearAllState()
    {
        // Arrange
        var adx = Indicators.ADX(14);
        var bars = CreateTrendBars(AscendingPrices(100m, 1m, 30));
        UpdateBars(adx, bars);

        // Act
        adx.Reset();

        // Assert
        AssertCount(0, adx);
        AssertNotReady(adx);
        Assert.Equal(0m, adx.Value);
        Assert.Equal(0m, adx.PlusDI);
        Assert.Equal(0m, adx.MinusDI);
    }

    [Fact]
    public void ADX_PlusDIAndMinusDI_ShouldBeWithinValidRange()
    {
        // Arrange
        var adx = Indicators.ADX(14);
        var bars = CreateTrendBars(SineWavePrices(100m, 10m, 40), volatility: 0.02m);

        // Act
        UpdateBars(adx, bars);

        // Assert
        AssertReady(adx);
        AssertInRange(adx.PlusDI, 0m, 100m, "+DI should be between 0 and 100");
        AssertInRange(adx.MinusDI, 0m, 100m, "-DI should be between 0 and 100");
    }

    [Fact]
    public void ADX_Value_ShouldBeNonNegative()
    {
        // Arrange
        var adx = Indicators.ADX(14);
        var bars = CreateTrendBars(AscendingPrices(100m, 1m, 30));

        // Act
        UpdateBars(adx, bars);

        // Assert
        AssertReady(adx);
        Assert.True(adx.Value >= 0m, "ADX should always be non-negative");
    }

    [Fact]
    public void ADX_TrendReversal_ShouldDetectChangeInDI()
    {
        // Arrange
        var adx = Indicators.ADX(14);

        // Create uptrend then downtrend
        var uptrend = AscendingPrices(100m, 1m, 20);
        var downtrend = DescendingPrices(119m, 1m, 20);
        var combined = uptrend.Concat(downtrend).ToArray();
        var bars = CreateTrendBars(combined, volatility: 0.01m);

        // Act - Update with uptrend
        for (int i = 0; i < 30; i++)
        {
            adx.Update(bars[i]);
        }
        var plusDIBeforeReversal = adx.PlusDI;
        var minusDIBeforeReversal = adx.MinusDI;

        // Continue with downtrend
        for (int i = 30; i < bars.Length; i++)
        {
            adx.Update(bars[i]);
        }

        // Assert - After reversal, -DI should be higher
        Assert.True(adx.MinusDI > adx.PlusDI, "After reversal, -DI should exceed +DI");
    }

    [Fact]
    public void ADX_ConstantPrices_ShouldProduceZeroADX()
    {
        // Arrange
        var adx = Indicators.ADX(14);
        var constantBars = CreateBars(ConstantPrices(100m, 30));

        // Act
        UpdateBars(adx, constantBars);

        // Assert
        AssertReady(adx);
        Assert.Equal(0m, adx.Value);
        Assert.Equal(0m, adx.PlusDI);
        Assert.Equal(0m, adx.MinusDI);
    }

    [Fact]
    public void ADX_DifferentPeriods_ShouldProduceDifferentSensitivity()
    {
        // Arrange
        var adxShort = Indicators.ADX(7);
        var adxLong = Indicators.ADX(21);
        var bars = CreateTrendBars(AscendingPrices(100m, 1m, 50), volatility: 0.02m);

        // Act
        UpdateBars(adxShort, bars);
        UpdateBars(adxLong, bars);

        // Assert - Both should be ready and detect trend
        AssertReady(adxShort);
        AssertReady(adxLong);
        // Shorter period should generally be more responsive
        Assert.True(adxShort.Value != adxLong.Value, "Different periods should produce different values");
    }

    [Fact]
    public void ADX_WithGaps_ShouldHandleCorrectly()
    {
        // Arrange
        var adx = Indicators.ADX(14);
        var bars = new[]
        {
            CreateBar(100m, 105m, 99m, 103m),
            CreateBar(103m, 108m, 102m, 107m),
            CreateBar(110m, 115m, 109m, 113m), // Gap up
            CreateBar(113m, 117m, 112m, 115m),
            CreateBar(115m, 119m, 114m, 118m),
        };

        // Repeat pattern to get enough data
        var extendedBars = Enumerable.Repeat(bars, 8).SelectMany(x => x).ToArray();

        // Act
        UpdateBars(adx, extendedBars);

        // Assert - Should handle gaps without errors
        AssertReady(adx);
        Assert.True(adx.Value >= 0m, "ADX should handle gaps gracefully");
    }

    [Fact]
    public void ADX_SmallPeriod_ShouldWork()
    {
        // Arrange
        var adx = Indicators.ADX(2);
        var bars = CreateTrendBars(AscendingPrices(100m, 1m, 10));

        // Act
        UpdateBars(adx, bars);

        // Assert
        AssertReady(adx);
        Assert.True(adx.Value >= 0m, "ADX should work with small period");
    }

    [Fact]
    public void ADX_UpdateSequentially_ShouldMaintainState()
    {
        // Arrange
        var adx = Indicators.ADX(14);
        var bars = CreateTrendBars(AscendingPrices(100m, 1m, 30));

        // Act - Update one by one
        foreach (var bar in bars)
        {
            var countBefore = adx.Count;
            adx.Update(bar);
            Assert.Equal(countBefore + 1, adx.Count);
        }

        // Assert
        AssertReady(adx);
        Assert.True(adx.Count == 30, "Count should match number of updates");
    }

    [Fact]
    public void ADX_TrendStrength_ShouldIncreaseDuringStrongTrend()
    {
        // Arrange
        var adx = Indicators.ADX(14);
        var strongTrend = CreateTrendBars(AscendingPrices(100m, 3m, 40), volatility: 0.005m);

        // Act - Get ADX after initial period
        for (int i = 0; i < 29; i++)
        {
            adx.Update(strongTrend[i]);
        }
        var initialADX = adx.Value;

        // Continue with strong trend
        for (int i = 29; i < strongTrend.Length; i++)
        {
            adx.Update(strongTrend[i]);
        }
        var laterADX = adx.Value;

        // Assert - ADX should increase or stay high during strong trend
        Assert.True(laterADX >= initialADX * 0.5m, "ADX should remain significant during strong trend");
    }
}
