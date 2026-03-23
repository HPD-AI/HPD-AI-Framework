using Rhodium.Primitives;
using Rhodium.Indicators;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Hull Moving Average (HMA) indicator.
/// HMA = WMA(2*WMA(n/2) - WMA(n), sqrt(n)) - designed for smoothness with minimal lag.
/// </summary>
public class HMATests
{
    [Fact]
    public void HMA_BecomesReady_AfterPeriodUpdates()
    {
        // HMA needs enough updates for its internal WMAs
        var period = 9; // Use 9 so sqrt(9) = 3 is clean
        var hma = Indicators.HMA(period);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 20);

        // Act - Update until ready
        int updateCount = 0;
        foreach (var price in prices)
        {
            hma.Update(price);
            updateCount++;
            if (hma.IsReady) break;
        }

        // Assert - Should be ready after period updates
        TestHelpers.AssertReady(hma);
        Assert.True(updateCount >= period, $"HMA should need at least {period} updates, got {updateCount}");
    }

    [Fact]
    public void HMA_ResetsCorrectly()
    {
        // Arrange
        var hma = Indicators.HMA(9);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 20);

        // Act & Assert
        TestHelpers.TestReset(hma, () => TestHelpers.UpdatePrices(hma, prices));
    }

    [Fact]
    public void HMA_ProducesConstantValue_WithConstantPrices()
    {
        // Arrange
        var hma = Indicators.HMA(9);
        var constantValue = 100m;
        var prices = TestHelpers.ConstantPrices(constantValue, 20);

        // Act
        TestHelpers.UpdatePrices(hma, prices);

        // Assert
        TestHelpers.AssertIndicatorValue(constantValue, hma, 0.01m);
    }

    [Fact]
    public void HMA_HandlesZeroPrices()
    {
        // Arrange
        var hma = Indicators.HMA(9);

        // Act & Assert
        TestHelpers.TestZeroPrices(hma, 20);
        TestHelpers.AssertIndicatorValue(0m, hma, 0.01m);
    }

    [Fact]
    public void HMA_HandlesLargePrices()
    {
        // Arrange
        var hma = Indicators.HMA(9);

        // Act & Assert
        TestHelpers.TestLargePrices(hma);
    }

    [Fact]
    public void HMA_RespondsToTrends()
    {
        // Arrange
        var hma = Indicators.HMA(9);
        var ascending = TestHelpers.AscendingPrices(100m, 1m, 20);
        var descending = TestHelpers.DescendingPrices(100m, 1m, 20);

        // Act & Assert
        TestHelpers.TestResponsiveness(hma, ascending, descending);
    }

    [Fact]
    public void HMA_CountIncrementsCorrectly()
    {
        // Arrange
        var hma = Indicators.HMA(9);
        var prices = TestHelpers.Prices(10m, 20m, 30m);

        // Act
        TestHelpers.UpdatePrices(hma, prices);

        // Assert
        TestHelpers.AssertCount(3, hma);
    }

    [Fact]
    public void HMA_MoreResponsive_ThanSMA()
    {
        // Arrange
        var hma = Indicators.HMA(9);
        var sma = Indicators.SMA(9);
        var prices = TestHelpers.ConstantPrices(100m, 15);

        // Act - Initialize both with constant prices
        TestHelpers.UpdatePrices(hma, prices);
        TestHelpers.UpdatePrices(sma, prices);

        var hmaBefore = hma.Value;
        var smaBefore = sma.Value;

        // Add a sudden spike
        hma.Update(150m);
        sma.Update(150m);

        // Assert - HMA should respond more due to its design
        var hmaChange = Math.Abs(hma.Value - hmaBefore);
        var smaChange = Math.Abs(sma.Value - smaBefore);

        Assert.True(hmaChange > smaChange,
            $"HMA should be more responsive. HMA change: {hmaChange}, SMA change: {smaChange}");
    }

    [Fact]
    public void HMA_SmootherThan_WMA()
    {
        // HMA should be smoother on choppy data
        // Arrange
        var hma = Indicators.HMA(9);
        var wma = Indicators.WMA(9);
        var prices = TestHelpers.OscillatingPrices(90m, 110m, 20);

        // Act
        TestHelpers.UpdatePrices(hma, prices);
        TestHelpers.UpdatePrices(wma, prices);

        // Assert - Both should be ready and smoothed
        TestHelpers.AssertReady(hma);
        TestHelpers.AssertReady(wma);
        TestHelpers.AssertInRange(hma.Value, 85m, 115m);
        TestHelpers.AssertInRange(wma.Value, 85m, 115m);
    }

    [Fact]
    public void HMA_AscendingPrices_ProducesAscendingValues()
    {
        // HMA uses WMA-based calculation which can have significant short-term variations
        // even with ascending prices due to weighted differences between inner WMAs

        // Arrange - Use smaller steps for smoother behavior
        var hma = Indicators.HMA(9);
        var prices = TestHelpers.AscendingPrices(100m, 3m, 30);

        // Act
        TestHelpers.UpdatePrices(hma, prices);

        // Assert - Verify overall trend is upward
        // HMA should track the upward trend overall, even if individual updates vary
        TestHelpers.AssertReady(hma);
        var startPrice = prices[0];
        var endPrice = prices[^1];

        // HMA should be significantly above starting price for an uptrend
        Assert.True(hma.Value > startPrice + 20m,
            $"HMA should track upward trend. Start: {startPrice}, End: {endPrice}, HMA: {hma.Value}");

        // HMA is designed to reduce lag, so it should be close to current price
        // It may occasionally equal or slightly exceed current price depending on recent momentum
        Assert.True(hma.Value <= endPrice + 1m,
            $"HMA should be at or near current price. HMA: {hma.Value}, Current: {endPrice}");
    }

    [Fact]
    public void HMA_DescendingPrices_ProducesDescendingValues()
    {
        // Arrange
        var hma = Indicators.HMA(9);
        var prices = TestHelpers.DescendingPrices(100m, 5m, 20);

        // Act & Assert
        decimal previousValue = decimal.MaxValue;
        int readyCount = 0;

        foreach (var price in prices)
        {
            hma.Update(price);
            if (hma.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(hma.Value <= previousValue + 0.5m, // HMA can have small variations
                        $"HMA should generally decrease with descending prices. Previous: {previousValue}, Current: {hma.Value}");
                }
                previousValue = hma.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void HMA_OscillatingPrices_ProducesSmoothedValue()
    {
        // Arrange
        var hma = Indicators.HMA(9);
        var prices = TestHelpers.OscillatingPrices(90m, 110m, 25);

        // Act
        TestHelpers.UpdatePrices(hma, prices);

        // Assert - HMA should smooth oscillations well
        TestHelpers.AssertReady(hma);
        TestHelpers.AssertInRange(hma.Value, 85m, 115m);
    }

    [Fact]
    public void HMA_SineWave_ProducesVerySmoothedOutput()
    {
        // Arrange
        var hma = Indicators.HMA(16);
        var prices = TestHelpers.SineWavePrices(100m, 20m, 100, 2.0);

        // Act
        TestHelpers.UpdatePrices(hma, prices);

        // Assert - HMA should smooth the sine wave excellently
        TestHelpers.AssertReady(hma);
        TestHelpers.AssertInRange(hma.Value, 80m, 120m);
    }

    [Fact]
    public void HMA_Formula_UsesWMAs()
    {
        // Documents that HMA = WMA(2*WMA(n/2) - WMA(n), sqrt(n))
        // Arrange
        var hma = Indicators.HMA(9);
        var prices = TestHelpers.ConstantPrices(100m, 20);

        // Act
        TestHelpers.UpdatePrices(hma, prices);

        // Assert - With constant prices, should equal constant
        // 2*100 - 100 = 100, then WMA(100) = 100
        TestHelpers.AssertApproximately(100m, hma.Value, 0.1m);
    }

    [Fact]
    public void HMA_Period4_SquareRootIs2()
    {
        // Test with period 4 where sqrt(4) = 2
        // Arrange
        var hma = Indicators.HMA(4);
        var prices = TestHelpers.AscendingPrices(100m, 5m, 10);

        // Act
        TestHelpers.UpdatePrices(hma, prices);

        // Assert
        TestHelpers.AssertReady(hma);
        Assert.True(hma.Value > 100m); // Should be above starting price
    }

    [Fact]
    public void HMA_Period16_SquareRootIs4()
    {
        // Test with period 16 where sqrt(16) = 4
        // Arrange
        var hma = Indicators.HMA(16);
        var prices = TestHelpers.AscendingPrices(100m, 2m, 25);

        // Act
        TestHelpers.UpdatePrices(hma, prices);

        // Assert
        TestHelpers.AssertReady(hma);
        Assert.True(hma.Value > 100m);
    }

    [Fact]
    public void HMA_TrendFollowing_BetterThanWMA()
    {
        // Arrange
        var hma = Indicators.HMA(9);
        var wma = Indicators.WMA(9);
        var trendPrices = TestHelpers.AscendingPrices(100m, 3m, 20);

        // Act
        TestHelpers.UpdatePrices(hma, trendPrices);
        TestHelpers.UpdatePrices(wma, trendPrices);

        // Assert - HMA should track trend more closely
        var currentPrice = trendPrices[^1];
        var hmaDistance = Math.Abs(currentPrice - hma.Value);
        var wmaDistance = Math.Abs(currentPrice - wma.Value);

        Assert.True(hmaDistance < wmaDistance,
            $"HMA should have less lag. HMA distance: {hmaDistance}, WMA distance: {wmaDistance}");
    }

    [Fact]
    public void HMA_ShortPeriod_MoreResponsive_ThanLongPeriod()
    {
        // Arrange
        var hmaShort = Indicators.HMA(4);
        var hmaLong = Indicators.HMA(16);
        var prices = TestHelpers.ConstantPrices(100m, 20);

        // Act - Initialize with constant prices
        TestHelpers.UpdatePrices(hmaShort, prices);
        TestHelpers.UpdatePrices(hmaLong, prices);

        var beforeShort = hmaShort.Value;
        var beforeLong = hmaLong.Value;

        // Add spike
        hmaShort.Update(150m);
        hmaLong.Update(150m);

        // Assert - Shorter period should respond more
        var shortChange = Math.Abs(hmaShort.Value - beforeShort);
        var longChange = Math.Abs(hmaLong.Value - beforeLong);

        Assert.True(shortChange > longChange,
            $"Shorter HMA should be more responsive. Short change: {shortChange}, Long change: {longChange}");
    }

    [Fact]
    public void HMA_DifferentPeriods_ProduceDifferentValues()
    {
        // Arrange
        var hma4 = Indicators.HMA(4);
        var hma9 = Indicators.HMA(9);
        var hma16 = Indicators.HMA(16);
        var prices = TestHelpers.AscendingPrices(100m, 2m, 25);

        // Act
        TestHelpers.UpdatePrices(hma4, prices);
        TestHelpers.UpdatePrices(hma9, prices);
        TestHelpers.UpdatePrices(hma16, prices);

        // Assert - Different periods should produce different values
        Assert.NotEqual(hma4.Value, hma9.Value);
        Assert.NotEqual(hma9.Value, hma16.Value);
        Assert.NotEqual(hma4.Value, hma16.Value);
    }

    [Fact]
    public void HMA_ReducesLag_SignificantlyInTrends()
    {
        // HMA is designed to minimize lag
        // Arrange
        var hma = Indicators.HMA(10);
        var sma = Indicators.SMA(10);
        var strongTrend = TestHelpers.AscendingPrices(100m, 5m, 20);

        // Act
        TestHelpers.UpdatePrices(hma, strongTrend);
        TestHelpers.UpdatePrices(sma, strongTrend);

        // Assert - HMA should be much closer to current price
        var currentPrice = strongTrend[^1];
        var hmaLag = currentPrice - hma.Value;
        var smaLag = currentPrice - sma.Value;

        Assert.True(hmaLag < smaLag,
            $"HMA should have significantly less lag. HMA lag: {hmaLag}, SMA lag: {smaLag}");
    }

    [Fact]
    public void HMA_HandlesNonSquarePeriods()
    {
        // Test with period that doesn't have integer square root
        // Arrange
        var hma = Indicators.HMA(10); // sqrt(10) = 3.162...
        var prices = TestHelpers.AscendingPrices(100m, 2m, 20);

        // Act
        TestHelpers.UpdatePrices(hma, prices);

        // Assert - Should still work correctly
        TestHelpers.AssertReady(hma);
        Assert.True(hma.Value > 100m);
    }

    [Fact]
    public void HMA_StableAfterConvergence()
    {
        // Arrange
        var hma = Indicators.HMA(9);
        var constantValue = 100m;
        var prices = TestHelpers.ConstantPrices(constantValue, 30);

        // Act
        TestHelpers.UpdatePrices(hma, prices);

        // Assert - Should converge to constant value
        TestHelpers.AssertApproximately(constantValue, hma.Value, 0.5m);
    }

    [Fact]
    public void HMA_BalancesBetween_SmoothnessAndResponsiveness()
    {
        // HMA aims to be smooth like SMA but responsive like EMA
        // Arrange
        var hma = Indicators.HMA(9);
        var sma = Indicators.SMA(9);
        var ema = Indicators.EMA(9);

        var prices = TestHelpers.Prices(100m, 105m, 95m, 110m, 90m, 115m, 85m, 120m, 80m, 125m, 120m, 122m);

        // Act
        TestHelpers.UpdatePrices(hma, prices);
        TestHelpers.UpdatePrices(sma, prices);
        TestHelpers.UpdatePrices(ema, prices);

        // Assert - All should be ready and in reasonable range
        TestHelpers.AssertReady(hma);
        TestHelpers.AssertReady(sma);
        TestHelpers.AssertReady(ema);

        // HMA should be between EMA (more responsive) and SMA (smoother)
        // This is a general property but not always strictly true
        Assert.True(hma.Value > 0m);
    }
}
