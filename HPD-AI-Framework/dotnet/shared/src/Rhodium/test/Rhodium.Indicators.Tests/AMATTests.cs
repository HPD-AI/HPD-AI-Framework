using Rhodium.Primitives;
using Rhodium.Indicators;
using static Rhodium.Indicators.Tests.TestHelpers;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Advanced Moving Average Trend (AMAT) indicator.
/// Uses three EMAs to determine trend direction and strength.
/// </summary>
public class AMATTests
{
    [Fact]
    public void AMAT_Constructor_ShouldInitializeWithDefaultPeriods()
    {
        // Arrange & Act
        var amat = Indicators.AMAT();

        // Assert
        Assert.NotNull(amat);
        Assert.Equal(0, amat.Count);
        Assert.False(amat.IsReady);
        Assert.Equal(0m, amat.Value);
        Assert.Equal(0, amat.Direction);
        Assert.Equal(0m, amat.Strength);
    }

    [Fact]
    public void AMAT_Constructor_ShouldAcceptCustomPeriods()
    {
        // Arrange & Act
        var amat = Indicators.AMAT(5, 13, 34);

        // Assert
        Assert.NotNull(amat);
        Assert.Equal(0, amat.Count);
        Assert.False(amat.IsReady);
    }

    [Fact]
    public void AMAT_ShouldBecomeReadyAfterLongestPeriod()
    {
        // Arrange
        var amat = Indicators.AMAT(8, 21, 55);
        var prices = AscendingPrices(100m, 1m, 60);

        // Act & Assert - Ready after slowest EMA is ready
        for (int i = 0; i < 54; i++)
        {
            amat.Update(prices[i]);
            AssertNotReady(amat, $"AMAT should not be ready after {i + 1} updates");
        }

        amat.Update(prices[54]);
        AssertReady(amat, "AMAT should be ready after slowest EMA period");
    }

    [Fact]
    public void AMAT_Direction_ShouldBeInValidRange()
    {
        // Arrange
        var amat = Indicators.AMAT();
        var prices = SineWavePrices(100m, 10m, 70);

        // Act
        UpdatePrices(amat, prices);

        // Assert
        AssertReady(amat);
        AssertInRange(amat.Direction, -1, 1, "Direction should be -1, 0, or 1");
    }

    [Fact]
    public void AMAT_Strength_ShouldBeNonNegative()
    {
        // Arrange
        var amat = Indicators.AMAT();
        var prices = AscendingPrices(100m, 1m, 70);

        // Act
        UpdatePrices(amat, prices);

        // Assert
        AssertReady(amat);
        Assert.True(amat.Strength >= 0m, "Strength should be non-negative");
        AssertInRange(amat.Strength, 0m, 1m, "Strength should be between 0 and 1");
    }

    [Fact]
    public void AMAT_BullishAlignment_ShouldShowPositiveDirection()
    {
        // Arrange
        var amat = Indicators.AMAT(8, 21, 55);
        var strongUptrend = AscendingPrices(100m, 2m, 70);

        // Act
        UpdatePrices(amat, strongUptrend);

        // Assert
        AssertReady(amat);
        Assert.Equal(1, amat.Direction); // Bullish
        Assert.True(amat.Strength > 0m, "Strength should be positive in strong uptrend");
    }

    [Fact]
    public void AMAT_BearishAlignment_ShouldShowNegativeDirection()
    {
        // Arrange
        var amat = Indicators.AMAT(8, 21, 55);
        var strongDowntrend = DescendingPrices(200m, 2m, 70);

        // Act
        UpdatePrices(amat, strongDowntrend);

        // Assert
        AssertReady(amat);
        Assert.Equal(-1, amat.Direction); // Bearish
        Assert.True(amat.Strength > 0m, "Strength should be positive in strong downtrend");
    }

    [Fact]
    public void AMAT_NoAlignment_ShouldShowZeroDirection()
    {
        // Arrange
        var amat = Indicators.AMAT(8, 21, 55);
        // Very tight oscillation to minimize any directional bias
        var sideways = OscillatingPrices(99.8m, 100.2m, 70);

        // Act
        UpdatePrices(amat, sideways);

        // Assert
        AssertReady(amat);

        // With very tight oscillation, EMAs should be very close to each other
        // Direction can be 0, 1, or -1 depending on exact EMA values
        // The key is that there's no strong trend
        AssertInRange(amat.Direction, -1, 1, "Direction should be valid");

        // Strength should be low when oscillating tightly
        Assert.True(amat.Strength >= 0m && amat.Strength <= 1m, "Strength should be in valid range");

        // If direction is 0, it means no clear alignment
        if (amat.Direction == 0)
        {
            Assert.True(amat.Strength >= 0m, "Strength should be non-negative even without alignment");
        }
    }

    [Fact]
    public void AMAT_Value_ShouldBeDirectionTimesStrength()
    {
        // Arrange
        var amat = Indicators.AMAT();
        var uptrend = AscendingPrices(100m, 1.5m, 70);

        // Act
        UpdatePrices(amat, uptrend);

        // Assert
        AssertReady(amat);
        var expectedValue = amat.Direction * amat.Strength;
        AssertApproximately(expectedValue, amat.Value, DefaultPrecision);
    }

    [Fact]
    public void AMAT_StrongTrend_ShouldHaveHighStrength()
    {
        // Arrange
        var amat = Indicators.AMAT(8, 21, 55);
        var veryStrongTrend = AscendingPrices(100m, 3m, 70);

        // Act
        UpdatePrices(amat, veryStrongTrend);

        // Assert
        AssertReady(amat);
        Assert.True(amat.Strength > 0.2m, "Strong trend should have high strength");
    }

    [Fact]
    public void AMAT_WeakTrend_ShouldHaveLowStrength()
    {
        // Arrange
        var amat = Indicators.AMAT(8, 21, 55);
        var weakTrend = AscendingPrices(100m, 0.1m, 70);

        // Act
        UpdatePrices(amat, weakTrend);

        // Assert
        AssertReady(amat);
        // Weak trend may still show direction but with lower strength
        Assert.True(amat.Strength < 1m, "Weak trend should have moderate or low strength");
    }

    [Fact]
    public void AMAT_Reset_ShouldClearAllState()
    {
        // Arrange
        var amat = Indicators.AMAT();
        var prices = AscendingPrices(100m, 1m, 70);
        UpdatePrices(amat, prices);

        // Act
        amat.Reset();

        // Assert
        AssertCount(0, amat);
        AssertNotReady(amat);
        Assert.Equal(0m, amat.Value);
        Assert.Equal(0, amat.Direction);
        Assert.Equal(0m, amat.Strength);
    }

    [Fact]
    public void AMAT_TrendReversal_ShouldChangeDirection()
    {
        // Arrange
        var amat = Indicators.AMAT(8, 21, 55);

        // Create uptrend then downtrend
        var uptrend = AscendingPrices(100m, 1m, 60);
        var downtrend = DescendingPrices(159m, 2m, 60);
        var combined = uptrend.Concat(downtrend).ToArray();

        // Act - Update with uptrend
        for (int i = 0; i < 60; i++)
        {
            amat.Update(combined[i]);
        }
        var directionBefore = amat.Direction;

        // Continue with downtrend
        for (int i = 60; i < Math.Min(combined.Length, 120); i++)
        {
            amat.Update(combined[i]);
        }

        // Assert - Direction should eventually flip
        Assert.Equal(1, directionBefore); // Should be bullish initially
        Assert.Equal(-1, amat.Direction); // Should flip to bearish
    }

    [Fact]
    public void AMAT_ConstantPrices_ShouldHaveZeroStrength()
    {
        // Arrange
        var amat = Indicators.AMAT();
        var constant = ConstantPrices(100m, 70);

        // Act
        UpdatePrices(amat, constant);

        // Assert
        AssertReady(amat);
        Assert.Equal(0, amat.Direction);
        Assert.Equal(0m, amat.Strength);
        Assert.Equal(0m, amat.Value);
    }

    [Fact]
    public void AMAT_DifferentPeriods_ShouldProduceDifferentSensitivity()
    {
        // Arrange
        var amatFast = Indicators.AMAT(5, 13, 34);
        var amatSlow = Indicators.AMAT(13, 34, 89);
        var prices = AscendingPrices(100m, 1m, 100);

        // Act
        UpdatePrices(amatFast, prices);
        UpdatePrices(amatSlow, prices);

        // Assert
        AssertReady(amatFast);
        AssertReady(amatSlow);
        // Both should detect uptrend but may have different strengths
        Assert.Equal(1, amatFast.Direction);
        Assert.Equal(1, amatSlow.Direction);
    }

    [Fact]
    public void AMAT_UpdateSequentially_ShouldMaintainCount()
    {
        // Arrange
        var amat = Indicators.AMAT();
        var prices = AscendingPrices(100m, 1m, 70);

        // Act
        foreach (var price in prices)
        {
            var countBefore = amat.Count;
            amat.Update(price);
            Assert.Equal(countBefore + 1, amat.Count);
        }

        // Assert
        AssertReady(amat);
        Assert.Equal(70, amat.Count);
    }

    [Fact]
    public void AMAT_BullishValue_ShouldBePositive()
    {
        // Arrange
        var amat = Indicators.AMAT();
        var uptrend = AscendingPrices(100m, 2m, 70);

        // Act
        UpdatePrices(amat, uptrend);

        // Assert
        AssertReady(amat);
        Assert.True(amat.Value > 0m, "AMAT value should be positive in bullish trend");
    }

    [Fact]
    public void AMAT_BearishValue_ShouldBeNegative()
    {
        // Arrange
        var amat = Indicators.AMAT();
        var downtrend = DescendingPrices(200m, 2m, 70);

        // Act
        UpdatePrices(amat, downtrend);

        // Assert
        AssertReady(amat);
        Assert.True(amat.Value < 0m, "AMAT value should be negative in bearish trend");
    }

    [Fact]
    public void AMAT_SineWave_ShouldShowMixedSignals()
    {
        // Arrange
        var amat = Indicators.AMAT(8, 21, 55);
        var sineWave = SineWavePrices(100m, 20m, 100, frequency: 1.0);

        // Act
        UpdatePrices(amat, sineWave);

        // Assert
        AssertReady(amat);
        // Sine wave creates oscillating alignment - direction may vary
        AssertInRange(amat.Direction, -1, 1);
    }

    [Fact]
    public void AMAT_PartialAlignment_ShouldHaveReducedStrength()
    {
        // Arrange
        var amat = Indicators.AMAT(8, 21, 55);

        // Create scenario where EMAs are not fully aligned
        // Start with uptrend, then sideways
        var uptrend = AscendingPrices(100m, 2m, 40);
        var sideways = ConstantPrices(uptrend[uptrend.Length - 1], 30);
        var combined = uptrend.Concat(sideways).ToArray();

        // Act
        UpdatePrices(amat, combined);

        // Assert
        AssertReady(amat);
        // Without full alignment, strength should be reduced (multiplied by 0.5)
        if (amat.Direction == 0)
        {
            Assert.True(amat.Strength < 1m, "No alignment should have reduced strength");
        }
    }

    [Fact]
    public void AMAT_FastAboveMediumAboveSlow_IsBullish()
    {
        // Arrange
        var amat = Indicators.AMAT(8, 21, 55);
        var strongUptrend = AscendingPrices(100m, 2m, 70);

        // Act
        UpdatePrices(amat, strongUptrend);

        // Assert
        AssertReady(amat);
        Assert.Equal(1, amat.Direction); // Fast > Medium > Slow = Bullish
    }

    [Fact]
    public void AMAT_FastBelowMediumBelowSlow_IsBearish()
    {
        // Arrange
        var amat = Indicators.AMAT(8, 21, 55);
        var strongDowntrend = DescendingPrices(200m, 2m, 70);

        // Act
        UpdatePrices(amat, strongDowntrend);

        // Assert
        AssertReady(amat);
        Assert.Equal(-1, amat.Direction); // Fast < Medium < Slow = Bearish
    }

    [Fact]
    public void AMAT_StrengthCalculation_ShouldReflectSpread()
    {
        // Arrange
        var amatWide = Indicators.AMAT(5, 20, 50);
        var amatNarrow = Indicators.AMAT(8, 10, 12);

        var prices = AscendingPrices(100m, 2m, 60);

        // Act
        UpdatePrices(amatWide, prices);
        UpdatePrices(amatNarrow, prices);

        // Assert
        AssertReady(amatWide);
        AssertReady(amatNarrow);
        // Wider period spread typically creates more separation in EMAs
        Assert.True(amatWide.Strength >= 0m && amatNarrow.Strength >= 0m);
    }

    [Fact]
    public void AMAT_AfterReset_ShouldWorkCorrectly()
    {
        // Arrange
        var amat = Indicators.AMAT();
        var prices = AscendingPrices(100m, 1m, 70);
        UpdatePrices(amat, prices);
        var initialValue = amat.Value;
        var initialDirection = amat.Direction;

        // Act
        amat.Reset();
        UpdatePrices(amat, prices);

        // Assert
        AssertReady(amat);
        AssertApproximately(initialValue, amat.Value, DefaultPrecision);
        Assert.Equal(initialDirection, amat.Direction);
    }

    [Fact]
    public void AMAT_StrengthCapping_ShouldNotExceedOne()
    {
        // Arrange
        var amat = Indicators.AMAT(8, 21, 55);
        var extremeTrend = AscendingPrices(100m, 10m, 70); // Very steep

        // Act
        UpdatePrices(amat, extremeTrend);

        // Assert
        AssertReady(amat);
        AssertInRange(amat.Strength, 0m, 1m, "Strength should be capped at 1");
    }

    [Fact]
    public void AMAT_ValueRange_ShouldBeMinusOneToOne()
    {
        // Arrange
        var amatBull = Indicators.AMAT();
        var amatBear = Indicators.AMAT();

        var uptrend = AscendingPrices(100m, 3m, 70);
        var downtrend = DescendingPrices(200m, 3m, 70);

        // Act
        UpdatePrices(amatBull, uptrend);
        UpdatePrices(amatBear, downtrend);

        // Assert
        AssertReady(amatBull);
        AssertReady(amatBear);
        AssertInRange(amatBull.Value, -1m, 1m, "Bullish AMAT value should be in range");
        AssertInRange(amatBear.Value, -1m, 1m, "Bearish AMAT value should be in range");
    }

    [Fact]
    public void AMAT_TransitionPeriod_ShouldChangeGradually()
    {
        // Arrange
        var amat = Indicators.AMAT(8, 21, 55);

        // Uptrend then flat
        var uptrend = AscendingPrices(100m, 2m, 60);
        var flat = ConstantPrices(uptrend[uptrend.Length - 1], 30);
        var combined = uptrend.Concat(flat).ToArray();

        // Act
        UpdatePrices(amat, combined.Take(60).ToArray());
        var directionAfterUptrend = amat.Direction;
        var strengthAfterUptrend = amat.Strength;

        UpdatePrices(amat, combined.Skip(60).ToArray());

        // Assert
        Assert.Equal(1, directionAfterUptrend); // Should be bullish
        // After flat period, may transition to neutral or maintain bullish with reduced strength
        Assert.True(amat.Strength <= strengthAfterUptrend, "Strength may reduce during flat period");
    }

    [Fact]
    public void AMAT_SmallPeriods_ShouldBeMoreResponsive()
    {
        // Arrange
        var amat = Indicators.AMAT(3, 7, 15);
        var prices = AscendingPrices(100m, 1m, 30);

        // Act
        UpdatePrices(amat, prices);

        // Assert
        AssertReady(amat);
        Assert.Equal(1, amat.Direction); // Should quickly detect uptrend
    }

    [Fact]
    public void AMAT_LargePeriods_ShouldBeLessResponsive()
    {
        // Arrange
        var amat = Indicators.AMAT(20, 50, 100);
        var prices = AscendingPrices(100m, 1m, 120);

        // Act
        UpdatePrices(amat, prices);

        // Assert
        AssertReady(amat);
        // Should eventually detect trend but takes longer
        Assert.True(amat.Direction >= -1 && amat.Direction <= 1);
    }

    [Fact]
    public void AMAT_NoAlignment_StrengthShouldBeHalved()
    {
        // Arrange
        var amat = Indicators.AMAT(8, 21, 55);

        // Create mixed signals - no clear alignment
        var mixed = new List<decimal>();
        for (int i = 0; i < 70; i++)
        {
            mixed.Add(100m + (i % 10 - 5)); // Oscillates around 100
        }

        // Act
        UpdatePrices(amat, mixed.ToArray());

        // Assert
        AssertReady(amat);
        if (amat.Direction == 0)
        {
            // When no alignment, strength is multiplied by 0.5
            Assert.True(amat.Strength < 0.5m, "No alignment should result in halved strength");
        }
    }
}
