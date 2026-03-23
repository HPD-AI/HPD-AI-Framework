using Rhodium.Primitives;
using Rhodium.Indicators;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Simple Moving Average (SMA) indicator.
/// </summary>
public class SMATests
{
    [Fact]
    public void SMA_CalculatesCorrectValue_WithSimplePrices()
    {
        // Arrange
        var sma = Indicators.SMA(3);
        var prices = TestHelpers.Prices(10m, 20m, 30m);

        // Act
        TestHelpers.UpdatePrices(sma, prices);

        // Assert
        var expected = (10m + 20m + 30m) / 3m; // 20
        TestHelpers.AssertIndicatorValue(expected, sma);
    }

    [Fact]
    public void SMA_BecomesReady_AfterPeriodUpdates()
    {
        // Arrange
        var period = 5;
        var sma = Indicators.SMA(period);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 10);

        // Act & Assert
        TestHelpers.TestReadinessAfterPeriod(sma, period, prices);
    }

    [Fact]
    public void SMA_ResetsCorrectly()
    {
        // Arrange
        var sma = Indicators.SMA(3);
        var prices = TestHelpers.Prices(10m, 20m, 30m, 40m, 50m);

        // Act & Assert
        TestHelpers.TestReset(sma, () => TestHelpers.UpdatePrices(sma, prices));
    }

    [Fact]
    public void SMA_ProducesConstantValue_WithConstantPrices()
    {
        // Arrange
        var sma = Indicators.SMA(5);
        var constantValue = 100m;
        var prices = TestHelpers.ConstantPrices(constantValue, 10);

        // Act
        TestHelpers.UpdatePrices(sma, prices);

        // Assert
        TestHelpers.AssertIndicatorValue(constantValue, sma);
    }

    [Fact]
    public void SMA_HandlesZeroPrices()
    {
        // Arrange
        var sma = Indicators.SMA(5);

        // Act & Assert
        TestHelpers.TestZeroPrices(sma, 10);
        TestHelpers.AssertIndicatorValue(0m, sma);
    }

    [Fact]
    public void SMA_HandlesLargePrices()
    {
        // Arrange
        var sma = Indicators.SMA(5);

        // Act & Assert
        TestHelpers.TestLargePrices(sma);
    }

    [Fact]
    public void SMA_UpdatesCorrectly_WithSlidingWindow()
    {
        // Arrange
        var sma = Indicators.SMA(3);

        // Act - Update with [10, 20, 30]
        TestHelpers.UpdatePrices(sma, 10m, 20m, 30m);
        var firstValue = sma.Value; // (10+20+30)/3 = 20

        // Update with 40, window now [20, 30, 40]
        sma.Update(40m);
        var secondValue = sma.Value; // (20+30+40)/3 = 30

        // Assert
        TestHelpers.AssertApproximately(20m, firstValue);
        TestHelpers.AssertApproximately(30m, secondValue);
    }

    [Fact]
    public void SMA_MatchesManualCalculation()
    {
        // Arrange
        var sma = Indicators.SMA(5);
        var prices = TestHelpers.Prices(100m, 102m, 101m, 103m, 105m, 104m, 106m);

        // Act
        TestHelpers.UpdatePrices(sma, prices);

        // Assert - Should be average of last 5 prices: 103, 105, 104, 106
        var lastFive = prices.TakeLast(5).ToArray();
        var expected = TestHelpers.CalculateSMA(lastFive);
        TestHelpers.AssertIndicatorValue(expected, sma);
    }

    [Fact]
    public void SMA_RespondsToTrends()
    {
        // Arrange
        var sma = Indicators.SMA(5);
        var ascending = TestHelpers.AscendingPrices(100m, 1m, 10);
        var descending = TestHelpers.DescendingPrices(100m, 1m, 10);

        // Act & Assert
        TestHelpers.TestResponsiveness(sma, ascending, descending);
    }

    [Fact]
    public void SMA_CountIncrementsCorrectly()
    {
        // Arrange
        var sma = Indicators.SMA(5);
        var prices = TestHelpers.Prices(10m, 20m, 30m);

        // Act
        TestHelpers.UpdatePrices(sma, prices);

        // Assert
        TestHelpers.AssertCount(3, sma);
    }

    [Fact]
    public void SMA_Period1_EqualsCurrentPrice()
    {
        // Arrange
        var sma = Indicators.SMA(1);
        var prices = TestHelpers.Prices(10m, 20m, 30m, 40m, 50m);

        // Act
        foreach (var price in prices)
        {
            sma.Update(price);
            // Assert - With period 1, SMA should always equal current price
            TestHelpers.AssertIndicatorValue(price, sma);
        }
    }

    [Fact]
    public void SMA_OscillatingPrices_ProducesSmoothedValue()
    {
        // Arrange
        var sma = Indicators.SMA(5);
        var prices = TestHelpers.OscillatingPrices(90m, 110m, 10);

        // Act
        TestHelpers.UpdatePrices(sma, prices);

        // Assert - SMA should smooth out oscillations
        var smoothedValue = sma.Value;
        TestHelpers.AssertInRange(smoothedValue, 95m, 105m);
    }

    [Fact]
    public void SMA_LongPeriod_ProducesMoreSmoothing()
    {
        // Arrange
        var shortSma = Indicators.SMA(3);
        var longSma = Indicators.SMA(10);
        var prices = TestHelpers.Prices(100m, 105m, 95m, 110m, 90m, 115m, 85m, 120m, 80m, 125m, 130m);

        // Act
        TestHelpers.UpdatePrices(shortSma, prices);
        TestHelpers.UpdatePrices(longSma, prices);

        // Assert - Longer period should respond more slowly to recent spike
        // The last value (130) should have more influence on short SMA
        Assert.True(shortSma.Value != longSma.Value, "SMAs with different periods should produce different values");
    }

    [Fact]
    public void SMA_AscendingPrices_ProducesAscendingValues()
    {
        // Arrange
        var sma = Indicators.SMA(3);
        var prices = TestHelpers.AscendingPrices(100m, 5m, 8);

        // Act & Assert
        decimal previousValue = 0m;
        int readyCount = 0;

        foreach (var price in prices)
        {
            sma.Update(price);
            if (sma.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(sma.Value > previousValue,
                        $"SMA should increase with ascending prices. Previous: {previousValue}, Current: {sma.Value}");
                }
                previousValue = sma.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void SMA_DescendingPrices_ProducesDescendingValues()
    {
        // Arrange
        var sma = Indicators.SMA(3);
        var prices = TestHelpers.DescendingPrices(100m, 5m, 8);

        // Act & Assert
        decimal previousValue = decimal.MaxValue;
        int readyCount = 0;

        foreach (var price in prices)
        {
            sma.Update(price);
            if (sma.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(sma.Value < previousValue,
                        $"SMA should decrease with descending prices. Previous: {previousValue}, Current: {sma.Value}");
                }
                previousValue = sma.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void SMA_SineWave_ProducesSmoothedOutput()
    {
        // Arrange
        var sma = Indicators.SMA(5);
        var prices = TestHelpers.SineWavePrices(100m, 10m, 50, 2.0);

        // Act
        TestHelpers.UpdatePrices(sma, prices);

        // Assert - SMA should smooth the sine wave
        TestHelpers.AssertReady(sma);
        TestHelpers.AssertInRange(sma.Value, 90m, 110m);
    }
}
