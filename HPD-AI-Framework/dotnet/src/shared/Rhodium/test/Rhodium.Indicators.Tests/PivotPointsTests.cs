using Rhodium.Primitives;
using Rhodium.Indicators;
using static Rhodium.Indicators.Tests.TestHelpers;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Pivot Points indicator.
/// Calculates support and resistance levels based on previous bar's high, low, close.
/// </summary>
public class PivotPointsTests
{
    [Fact]
    public void PivotPoints_Constructor_ShouldInitialize()
    {
        // Arrange & Act
        var pivot = Indicators.PivotPoints();

        // Assert
        Assert.NotNull(pivot);
        Assert.Equal(0, pivot.Count);
        Assert.False(pivot.IsReady);
        Assert.Equal(0m, pivot.Value);
        Assert.Equal(0m, pivot.PP);
        Assert.Equal(0m, pivot.R1);
        Assert.Equal(0m, pivot.R2);
        Assert.Equal(0m, pivot.S1);
        Assert.Equal(0m, pivot.S2);
    }

    [Fact]
    public void PivotPoints_ShouldBecomeReadyAfterFirstBar()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bar = CreateBar(100m, 105m, 99m, 103m);

        // Act
        pivot.Update(bar);

        // Assert
        AssertReady(pivot);
        Assert.Equal(1, pivot.Count);
    }

    [Fact]
    public void PivotPoints_PP_ShouldBeAverageOfHighLowClose()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bar = CreateBar(100m, 105m, 99m, 102m);

        // Act
        pivot.Update(bar);

        // Assert
        var expected = (105m + 99m + 102m) / 3m;
        AssertApproximately(expected, pivot.PP, HighPrecision);
        AssertApproximately(expected, pivot.Value, HighPrecision);
    }

    [Fact]
    public void PivotPoints_R1_ShouldBeCalculatedCorrectly()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bar = CreateBar(100m, 105m, 99m, 102m);

        // Act
        pivot.Update(bar);

        // Assert
        var pp = (105m + 99m + 102m) / 3m;
        var expectedR1 = 2m * pp - 99m;
        AssertApproximately(expectedR1, pivot.R1, HighPrecision);
    }

    [Fact]
    public void PivotPoints_R2_ShouldBeCalculatedCorrectly()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bar = CreateBar(100m, 105m, 99m, 102m);

        // Act
        pivot.Update(bar);

        // Assert
        var pp = (105m + 99m + 102m) / 3m;
        var expectedR2 = pp + (105m - 99m);
        AssertApproximately(expectedR2, pivot.R2, HighPrecision);
    }

    [Fact]
    public void PivotPoints_S1_ShouldBeCalculatedCorrectly()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bar = CreateBar(100m, 105m, 99m, 102m);

        // Act
        pivot.Update(bar);

        // Assert
        var pp = (105m + 99m + 102m) / 3m;
        var expectedS1 = 2m * pp - 105m;
        AssertApproximately(expectedS1, pivot.S1, HighPrecision);
    }

    [Fact]
    public void PivotPoints_S2_ShouldBeCalculatedCorrectly()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bar = CreateBar(100m, 105m, 99m, 102m);

        // Act
        pivot.Update(bar);

        // Assert
        var pp = (105m + 99m + 102m) / 3m;
        var expectedS2 = pp - (105m - 99m);
        AssertApproximately(expectedS2, pivot.S2, HighPrecision);
    }

    [Fact]
    public void PivotPoints_LevelOrdering_ShouldBeCorrect()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bar = CreateBar(100m, 110m, 95m, 105m);

        // Act
        pivot.Update(bar);

        // Assert - R2 > R1 > PP > S1 > S2
        Assert.True(pivot.R2 > pivot.R1, "R2 should be above R1");
        Assert.True(pivot.R1 > pivot.PP, "R1 should be above PP");
        Assert.True(pivot.PP > pivot.S1, "PP should be above S1");
        Assert.True(pivot.S1 > pivot.S2, "S1 should be above S2");
    }

    [Fact]
    public void PivotPoints_Update_ShouldRecalculateAllLevels()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bar1 = CreateBar(100m, 105m, 99m, 103m);

        // Act
        pivot.Update(bar1);
        var pp1 = pivot.PP;
        var r1_1 = pivot.R1;
        var s1_1 = pivot.S1;

        var bar2 = CreateBar(103m, 110m, 102m, 108m);
        pivot.Update(bar2);

        // Assert - Values should change with new bar
        Assert.NotEqual(pp1, pivot.PP);
        Assert.NotEqual(r1_1, pivot.R1);
        Assert.NotEqual(s1_1, pivot.S1);
    }

    [Fact]
    public void PivotPoints_SymmetricBar_ShouldHaveSymmetricLevels()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bar = CreateBar(100m, 110m, 90m, 100m); // Close at open, symmetric range

        // Act
        pivot.Update(bar);

        // Assert - PP should be at center
        var pp = (110m + 90m + 100m) / 3m;
        AssertApproximately(100m, pp, HighPrecision);

        // R1 and S1 should be symmetric around PP
        var r1Distance = pivot.R1 - pivot.PP;
        var s1Distance = pivot.PP - pivot.S1;
        AssertApproximately(r1Distance, s1Distance, HighPrecision);
    }

    [Fact]
    public void PivotPoints_DojiBar_ShouldCalculate()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var dojiBar = CreateBar(100m, 100m, 100m, 100m); // All prices equal

        // Act
        pivot.Update(dojiBar);

        // Assert
        AssertApproximately(100m, pivot.PP, HighPrecision);
        AssertApproximately(100m, pivot.R1, HighPrecision);
        AssertApproximately(100m, pivot.R2, HighPrecision);
        AssertApproximately(100m, pivot.S1, HighPrecision);
        AssertApproximately(100m, pivot.S2, HighPrecision);
    }

    [Fact]
    public void PivotPoints_Reset_ShouldClearAllState()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bar = CreateBar(100m, 105m, 99m, 103m);
        pivot.Update(bar);

        // Act
        pivot.Reset();

        // Assert
        AssertCount(0, pivot);
        AssertNotReady(pivot);
        Assert.Equal(0m, pivot.Value);
        Assert.Equal(0m, pivot.PP);
        Assert.Equal(0m, pivot.R1);
        Assert.Equal(0m, pivot.R2);
        Assert.Equal(0m, pivot.S1);
        Assert.Equal(0m, pivot.S2);
    }

    [Fact]
    public void PivotPoints_BullishBar_ShouldCalculate()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bullishBar = CreateBullishBar(100m, 110m);

        // Act
        pivot.Update(bullishBar);

        // Assert
        AssertReady(pivot);
        Assert.True(pivot.PP > 0m, "PP should be calculated");
        Assert.True(pivot.R1 > pivot.PP, "R1 should be above PP");
        Assert.True(pivot.S1 < pivot.PP, "S1 should be below PP");
    }

    [Fact]
    public void PivotPoints_BearishBar_ShouldCalculate()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bearishBar = CreateBearishBar(110m, 100m);

        // Act
        pivot.Update(bearishBar);

        // Assert
        AssertReady(pivot);
        Assert.True(pivot.PP > 0m, "PP should be calculated");
        Assert.True(pivot.R1 > pivot.PP, "R1 should be above PP");
        Assert.True(pivot.S1 < pivot.PP, "S1 should be below PP");
    }

    [Fact]
    public void PivotPoints_SequentialUpdates_ShouldMaintainCount()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bars = CreateTrendBars(AscendingPrices(100m, 1m, 10));

        // Act
        foreach (var bar in bars)
        {
            var countBefore = pivot.Count;
            pivot.Update(bar);
            Assert.Equal(countBefore + 1, pivot.Count);
        }

        // Assert
        Assert.Equal(10, pivot.Count);
    }

    [Fact]
    public void PivotPoints_WideRangeBar_ShouldHaveWiderLevels()
    {
        // Arrange
        var pivotNarrow = Indicators.PivotPoints();
        var pivotWide = Indicators.PivotPoints();

        var narrowBar = CreateBar(100m, 102m, 99m, 101m);
        var wideBar = CreateBar(100m, 120m, 85m, 110m);

        // Act
        pivotNarrow.Update(narrowBar);
        pivotWide.Update(wideBar);

        // Assert
        var narrowSpread = pivotNarrow.R2 - pivotNarrow.S2;
        var wideSpread = pivotWide.R2 - pivotWide.S2;
        Assert.True(wideSpread > narrowSpread, "Wider bar should produce wider support/resistance levels");
    }

    [Fact]
    public void PivotPoints_PP_ShouldBeValueProperty()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bar = CreateBar(100m, 105m, 99m, 103m);

        // Act
        pivot.Update(bar);

        // Assert
        AssertApproximately(pivot.PP, pivot.Value, HighPrecision, "Value property should equal PP");
    }

    [Fact]
    public void PivotPoints_MultipleUpdates_ShouldUseLatestBar()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bars = new[]
        {
            CreateBar(100m, 105m, 99m, 103m),
            CreateBar(103m, 108m, 102m, 106m),
            CreateBar(106m, 111m, 105m, 109m),
        };

        // Act
        foreach (var bar in bars)
        {
            pivot.Update(bar);
        }

        // Assert - Should be based on last bar
        var lastBar = bars[bars.Length - 1];
        var expectedPP = (lastBar.High.Value + lastBar.Low.Value + lastBar.Close.Value) / 3m;
        AssertApproximately(expectedPP, pivot.PP, HighPrecision);
    }

    [Fact]
    public void PivotPoints_LargePrices_ShouldHandleWithoutOverflow()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bar = CreateBar(1000000m, 1000100m, 999900m, 1000050m);

        // Act
        pivot.Update(bar);

        // Assert
        AssertReady(pivot);
        Assert.True(pivot.PP > 0m, "Should handle large prices");
        Assert.True(pivot.R2 > pivot.R1 && pivot.R1 > pivot.PP, "Levels should be ordered correctly");
    }

    [Fact]
    public void PivotPoints_SmallPrices_ShouldMaintainPrecision()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bar = CreateBar(0.01m, 0.012m, 0.009m, 0.011m);

        // Act
        pivot.Update(bar);

        // Assert
        AssertReady(pivot);
        var expectedPP = (0.012m + 0.009m + 0.011m) / 3m;
        AssertApproximately(expectedPP, pivot.PP, HighPrecision);
    }

    [Fact]
    public void PivotPoints_R2MinusS2_ShouldEqualTwiceRange()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bar = CreateBar(100m, 110m, 95m, 105m);

        // Act
        pivot.Update(bar);

        // Assert
        var range = 110m - 95m;
        var r2MinusS2 = pivot.R2 - pivot.S2;
        AssertApproximately(2m * range, r2MinusS2, HighPrecision, "R2 - S2 should equal twice the range");
    }

    [Fact]
    public void PivotPoints_AfterReset_ShouldWorkCorrectly()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bar = CreateBar(100m, 105m, 99m, 103m);
        pivot.Update(bar);
        var initialPP = pivot.PP;

        // Act
        pivot.Reset();
        pivot.Update(bar);

        // Assert
        AssertApproximately(initialPP, pivot.PP, HighPrecision, "Should produce same result after reset");
    }

    [Fact]
    public void PivotPoints_DailyTradingScenario_ShouldProvideLevels()
    {
        // Arrange - Simulate previous day's data
        var pivot = Indicators.PivotPoints();
        var previousDay = CreateBar(100m, 115m, 98m, 110m); // High: 115, Low: 98, Close: 110

        // Act
        pivot.Update(previousDay);

        // Assert - Should provide tradeable levels for next day
        AssertReady(pivot);
        Assert.True(pivot.R1 > pivot.PP, "R1 is first resistance");
        Assert.True(pivot.R2 > pivot.R1, "R2 is second resistance");
        Assert.True(pivot.S1 < pivot.PP, "S1 is first support");
        Assert.True(pivot.S2 < pivot.S1, "S2 is second support");

        // PP should be within the range
        AssertInRange(pivot.PP, 98m, 115m, "PP should be within previous bar's range");
    }

    [Fact]
    public void PivotPoints_CloseAtHigh_ShouldBiasUpward()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bar = CreateBar(100m, 110m, 95m, 110m); // Close at high

        // Act
        pivot.Update(bar);

        // Assert
        var pp = (110m + 95m + 110m) / 3m;
        // PP should be biased toward high
        Assert.True(pp > 102.5m, "PP should be above midpoint when close is at high");
    }

    [Fact]
    public void PivotPoints_CloseAtLow_ShouldBiasDownward()
    {
        // Arrange
        var pivot = Indicators.PivotPoints();
        var bar = CreateBar(100m, 110m, 95m, 95m); // Close at low

        // Act
        pivot.Update(bar);

        // Assert
        var pp = (110m + 95m + 95m) / 3m;
        // PP should be biased toward low
        Assert.True(pp < 102.5m, "PP should be below midpoint when close is at low");
    }
}
