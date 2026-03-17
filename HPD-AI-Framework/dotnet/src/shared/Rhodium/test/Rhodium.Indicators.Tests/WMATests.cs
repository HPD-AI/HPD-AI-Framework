using Rhodium.Primitives;
using Rhodium.Indicators;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Weighted Moving Average (WMA) indicator.
/// WMA gives linearly increasing weights to recent prices.
/// Also known as LWMA (Linear Weighted Moving Average).
/// </summary>
public class WMATests
{
    [Fact]
    public void WMA_BecomesReady_AfterPeriodUpdates()
    {
        // Arrange
        var period = 5;
        var wma = Indicators.WMA(period);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 10);

        // Act & Assert
        TestHelpers.TestReadinessAfterPeriod(wma, period, prices);
    }

    [Fact]
    public void WMA_ResetsCorrectly()
    {
        // Arrange
        var wma = Indicators.WMA(5);
        var prices = TestHelpers.Prices(10m, 20m, 30m, 40m, 50m);

        // Act & Assert
        TestHelpers.TestReset(wma, () => TestHelpers.UpdatePrices(wma, prices));
    }

    [Fact]
    public void WMA_CalculatesCorrectValue_WithSimpleSequence()
    {
        // Arrange
        var wma = Indicators.WMA(3);
        var prices = TestHelpers.Prices(10m, 20m, 30m);

        // Act
        TestHelpers.UpdatePrices(wma, prices);

        // Assert
        // WMA = (10*1 + 20*2 + 30*3) / (1+2+3) = (10 + 40 + 90) / 6 = 140 / 6 = 23.333...
        TestHelpers.AssertApproximately(23.333m, wma.Value, 0.01m);
    }

    [Fact]
    public void WMA_ProducesConstantValue_WithConstantPrices()
    {
        // Arrange
        var wma = Indicators.WMA(5);
        var constantValue = 100m;
        var prices = TestHelpers.ConstantPrices(constantValue, 10);

        // Act
        TestHelpers.UpdatePrices(wma, prices);

        // Assert - With constant prices, WMA equals the constant
        TestHelpers.AssertIndicatorValue(constantValue, wma);
    }

    [Fact]
    public void WMA_HandlesZeroPrices()
    {
        // Arrange
        var wma = Indicators.WMA(5);

        // Act & Assert
        TestHelpers.TestZeroPrices(wma, 10);
        TestHelpers.AssertIndicatorValue(0m, wma);
    }

    [Fact]
    public void WMA_HandlesLargePrices()
    {
        // Arrange
        var wma = Indicators.WMA(5);

        // Act & Assert
        TestHelpers.TestLargePrices(wma);
    }

    [Fact]
    public void WMA_RespondsToTrends()
    {
        // Arrange
        var wma = Indicators.WMA(5);
        var ascending = TestHelpers.AscendingPrices(100m, 1m, 10);
        var descending = TestHelpers.DescendingPrices(100m, 1m, 10);

        // Act & Assert
        TestHelpers.TestResponsiveness(wma, ascending, descending);
    }

    [Fact]
    public void WMA_CountIncrementsCorrectly()
    {
        // Arrange
        var wma = Indicators.WMA(5);
        var prices = TestHelpers.Prices(10m, 20m, 30m);

        // Act
        TestHelpers.UpdatePrices(wma, prices);

        // Assert
        TestHelpers.AssertCount(3, wma);
    }

    [Fact]
    public void WMA_MoreResponsive_ThanSMA()
    {
        // Arrange
        var wma = Indicators.WMA(5);
        var sma = Indicators.SMA(5);
        var prices = TestHelpers.ConstantPrices(100m, 5);

        // Act - Initialize both with constant prices
        TestHelpers.UpdatePrices(wma, prices);
        TestHelpers.UpdatePrices(sma, prices);

        var wmaBeforeSpike = wma.Value;
        var smaBeforeSpike = sma.Value;

        // Add a sudden spike
        wma.Update(150m);
        sma.Update(150m);

        // Assert - WMA should respond more to recent price (highest weight)
        var wmaChange = Math.Abs(wma.Value - wmaBeforeSpike);
        var smaChange = Math.Abs(sma.Value - smaBeforeSpike);

        Assert.True(wmaChange > smaChange,
            $"WMA should be more responsive. WMA change: {wmaChange}, SMA change: {smaChange}");
    }

    [Fact]
    public void WMA_Period1_EqualsCurrentPrice()
    {
        // Arrange
        var wma = Indicators.WMA(1);
        var prices = TestHelpers.Prices(10m, 20m, 30m, 40m, 50m);

        // Act
        foreach (var price in prices)
        {
            wma.Update(price);
            // Assert - With period 1, WMA should always equal current price
            TestHelpers.AssertIndicatorValue(price, wma);
        }
    }

    [Fact]
    public void WMA_AscendingPrices_ProducesAscendingValues()
    {
        // Arrange
        var wma = Indicators.WMA(5);
        var prices = TestHelpers.AscendingPrices(100m, 5m, 10);

        // Act & Assert
        decimal previousValue = 0m;
        int readyCount = 0;

        foreach (var price in prices)
        {
            wma.Update(price);
            if (wma.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(wma.Value > previousValue,
                        $"WMA should increase with ascending prices. Previous: {previousValue}, Current: {wma.Value}");
                }
                previousValue = wma.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void WMA_DescendingPrices_ProducesDescendingValues()
    {
        // Arrange
        var wma = Indicators.WMA(5);
        var prices = TestHelpers.DescendingPrices(100m, 5m, 10);

        // Act & Assert
        decimal previousValue = decimal.MaxValue;
        int readyCount = 0;

        foreach (var price in prices)
        {
            wma.Update(price);
            if (wma.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(wma.Value < previousValue,
                        $"WMA should decrease with descending prices. Previous: {previousValue}, Current: {wma.Value}");
                }
                previousValue = wma.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void WMA_GivesMoreWeight_ToRecentPrices()
    {
        // Arrange
        var wma = Indicators.WMA(5);

        // Prices: [100, 100, 100, 100, 110]
        // WMA = (100*1 + 100*2 + 100*3 + 100*4 + 110*5) / 15
        //     = (100 + 200 + 300 + 400 + 550) / 15 = 1550 / 15 = 103.333...
        var prices = TestHelpers.Prices(100m, 100m, 100m, 100m, 110m);

        // Act
        TestHelpers.UpdatePrices(wma, prices);

        // Assert - WMA should be closer to 110 than simple average (102)
        var sma = TestHelpers.CalculateSMA(prices); // 102
        Assert.True(wma.Value > sma,
            $"WMA should be higher than SMA due to recent price weight. WMA: {wma.Value}, SMA: {sma}");
    }

    [Fact]
    public void WMA_WeightCalculation_Period4()
    {
        // Arrange
        var wma = Indicators.WMA(4);
        var prices = TestHelpers.Prices(10m, 20m, 30m, 40m);

        // Act
        TestHelpers.UpdatePrices(wma, prices);

        // Assert
        // WMA = (10*1 + 20*2 + 30*3 + 40*4) / (1+2+3+4)
        //     = (10 + 40 + 90 + 160) / 10 = 300 / 10 = 30
        TestHelpers.AssertIndicatorValue(30m, wma);
    }

    [Fact]
    public void WMA_UpdatesCorrectly_WithSlidingWindow()
    {
        // Arrange
        var wma = Indicators.WMA(3);

        // Act - First window [10, 20, 30]
        TestHelpers.UpdatePrices(wma, 10m, 20m, 30m);
        var firstValue = wma.Value;
        // WMA = (10*1 + 20*2 + 30*3) / 6 = 140 / 6 = 23.333...

        // Second window [20, 30, 40]
        wma.Update(40m);
        var secondValue = wma.Value;
        // WMA = (20*1 + 30*2 + 40*3) / 6 = 200 / 6 = 33.333...

        // Assert
        TestHelpers.AssertApproximately(23.333m, firstValue, 0.01m);
        TestHelpers.AssertApproximately(33.333m, secondValue, 0.01m);
    }

    [Fact]
    public void WMA_OscillatingPrices_ProducesSmoothedValue()
    {
        // Arrange
        var wma = Indicators.WMA(5);
        var prices = TestHelpers.OscillatingPrices(90m, 110m, 15);

        // Act
        TestHelpers.UpdatePrices(wma, prices);

        // Assert - WMA should smooth but stay responsive
        TestHelpers.AssertReady(wma);
        TestHelpers.AssertInRange(wma.Value, 85m, 115m);
    }

    [Fact]
    public void WMA_SineWave_ProducesSmoothedOutput()
    {
        // Arrange
        var wma = Indicators.WMA(10);
        var prices = TestHelpers.SineWavePrices(100m, 20m, 100, 2.0);

        // Act
        TestHelpers.UpdatePrices(wma, prices);

        // Assert - WMA should smooth the sine wave
        TestHelpers.AssertReady(wma);
        TestHelpers.AssertInRange(wma.Value, 80m, 120m);
    }

    [Fact]
    public void WMA_ComparesTo_EMA()
    {
        // Both WMA and EMA weight recent prices more, but with different formulas
        // Arrange
        var wma = Indicators.WMA(10);
        var ema = Indicators.EMA(10);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 15);

        // Act
        TestHelpers.UpdatePrices(wma, prices);
        TestHelpers.UpdatePrices(ema, prices);

        // Assert - Both should be responsive but WMA uses linear weights
        TestHelpers.AssertReady(wma);
        TestHelpers.AssertReady(ema);

        // Values will differ but both should be in reasonable range
        Assert.True(wma.Value > 100m && wma.Value < 115m);
        Assert.True(ema.Value > 100m && ema.Value < 115m);
    }

    [Fact]
    public void WMA_TriangularWeights_Period5()
    {
        // Document the triangular weight structure
        // Arrange
        var wma = Indicators.WMA(5);
        // Prices: oldest -> newest get weights 1, 2, 3, 4, 5
        var prices = TestHelpers.Prices(100m, 100m, 100m, 100m, 150m);

        // Act
        TestHelpers.UpdatePrices(wma, prices);

        // Assert
        // WMA = (100*1 + 100*2 + 100*3 + 100*4 + 150*5) / 15
        //     = (100 + 200 + 300 + 400 + 750) / 15 = 1750 / 15 = 116.666...
        TestHelpers.AssertApproximately(116.667m, wma.Value, 0.01m);
    }

    [Fact]
    public void WMA_ShortPeriod_MoreResponsive_ThanLongPeriod()
    {
        // Arrange
        var wmaShort = Indicators.WMA(3);
        var wmaLong = Indicators.WMA(10);
        var prices = TestHelpers.ConstantPrices(100m, 10);

        // Act - Initialize with constant prices
        TestHelpers.UpdatePrices(wmaShort, prices);
        TestHelpers.UpdatePrices(wmaLong, prices);

        var beforeShort = wmaShort.Value;
        var beforeLong = wmaLong.Value;

        // Add spike
        wmaShort.Update(150m);
        wmaLong.Update(150m);

        // Assert - Shorter period should respond more
        var shortChange = wmaShort.Value - beforeShort;
        var longChange = wmaLong.Value - beforeLong;

        Assert.True(shortChange > longChange,
            $"Shorter WMA should be more responsive. Short change: {shortChange}, Long change: {longChange}");
    }

    [Fact]
    public void WMA_DifferentPeriods_ProduceDifferentValues()
    {
        // Arrange
        var wma3 = Indicators.WMA(3);
        var wma10 = Indicators.WMA(10);
        var wma20 = Indicators.WMA(20);
        var prices = TestHelpers.AscendingPrices(100m, 2m, 25);

        // Act
        TestHelpers.UpdatePrices(wma3, prices);
        TestHelpers.UpdatePrices(wma10, prices);
        TestHelpers.UpdatePrices(wma20, prices);

        // Assert - Different periods should produce different values
        Assert.NotEqual(wma3.Value, wma10.Value);
        Assert.NotEqual(wma10.Value, wma20.Value);
        Assert.NotEqual(wma3.Value, wma20.Value);
    }

    [Fact]
    public void LWMA_IsAlias_ForWMA()
    {
        // Arrange
        var wma = Indicators.WMA(5);
        var lwma = Indicators.LWMA(5);
        var prices = TestHelpers.Prices(10m, 20m, 30m, 40m, 50m);

        // Act
        TestHelpers.UpdatePrices(wma, prices);
        TestHelpers.UpdatePrices(lwma, prices);

        // Assert - LWMA is just an alias, should produce identical results
        TestHelpers.AssertApproximately(lwma.Value, wma.Value, TestHelpers.HighPrecision);
    }
}
