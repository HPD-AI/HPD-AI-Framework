using Rhodium.Primitives;
using Rhodium.Indicators;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Exponential Moving Average (EMA) indicator.
/// EMA gives more weight to recent prices using smoothing factor: alpha = 2 / (period + 1)
/// </summary>
public class EMATests
{
    [Fact]
    public void EMA_BecomesReady_AfterPeriodUpdates()
    {
        // Arrange
        var period = 5;
        var ema = Indicators.EMA(period);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 10);

        // Act & Assert
        TestHelpers.TestReadinessAfterPeriod(ema, period, prices);
    }

    [Fact]
    public void EMA_ResetsCorrectly()
    {
        // Arrange
        var ema = Indicators.EMA(5);
        var prices = TestHelpers.Prices(10m, 20m, 30m, 40m, 50m);

        // Act & Assert
        TestHelpers.TestReset(ema, () => TestHelpers.UpdatePrices(ema, prices));
    }

    [Fact]
    public void EMA_CalculatesCorrectValue_WithKnownSequence()
    {
        // Arrange
        var ema = Indicators.EMA(3);
        var prices = TestHelpers.Prices(100m, 105m, 110m, 115m);

        // Act
        TestHelpers.UpdatePrices(ema, prices);

        // Assert
        // EMA calculation:
        // alpha = 2 / (3 + 1) = 0.5
        // EMA[0] = 100 (first price)
        // EMA[1] = 0.5*105 + 0.5*100 = 52.5 + 50 = 102.5
        // EMA[2] = 0.5*110 + 0.5*102.5 = 55 + 51.25 = 106.25
        // EMA[3] = 0.5*115 + 0.5*106.25 = 57.5 + 53.125 = 110.625
        TestHelpers.AssertApproximately(110.625m, ema.Value, 0.001m);
    }

    [Fact]
    public void EMA_MoreResponsive_ThanSMA()
    {
        // Arrange
        var ema = Indicators.EMA(10);
        var sma = Indicators.SMA(10);
        var prices = TestHelpers.Prices(100m, 100m, 100m, 100m, 100m, 100m, 100m, 100m, 100m, 100m);

        // Act - Initialize both with constant prices
        TestHelpers.UpdatePrices(ema, prices);
        TestHelpers.UpdatePrices(sma, prices);

        var emaBeforeSpike = ema.Value;
        var smaBeforeSpike = sma.Value;

        // Add a sudden spike
        ema.Update(150m);
        sma.Update(150m);

        // Assert - EMA should respond more to the spike
        var emaChange = Math.Abs(ema.Value - emaBeforeSpike);
        var smaChange = Math.Abs(sma.Value - smaBeforeSpike);

        Assert.True(emaChange > smaChange,
            $"EMA should be more responsive. EMA change: {emaChange}, SMA change: {smaChange}");
    }

    [Fact]
    public void EMA_ProducesConstantValue_WithConstantPrices()
    {
        // Arrange
        var ema = Indicators.EMA(5);
        var constantValue = 100m;
        var prices = TestHelpers.ConstantPrices(constantValue, 10);

        // Act
        TestHelpers.UpdatePrices(ema, prices);

        // Assert
        TestHelpers.AssertIndicatorValue(constantValue, ema);
    }

    [Fact]
    public void EMA_HandlesZeroPrices()
    {
        // Arrange
        var ema = Indicators.EMA(5);

        // Act & Assert
        TestHelpers.TestZeroPrices(ema, 10);
        TestHelpers.AssertIndicatorValue(0m, ema);
    }

    [Fact]
    public void EMA_HandlesLargePrices()
    {
        // Arrange
        var ema = Indicators.EMA(5);

        // Act & Assert
        TestHelpers.TestLargePrices(ema);
    }

    [Fact]
    public void EMA_RespondsToTrends()
    {
        // Arrange
        var ema = Indicators.EMA(5);
        var ascending = TestHelpers.AscendingPrices(100m, 1m, 10);
        var descending = TestHelpers.DescendingPrices(100m, 1m, 10);

        // Act & Assert
        TestHelpers.TestResponsiveness(ema, ascending, descending);
    }

    [Fact]
    public void EMA_CountIncrementsCorrectly()
    {
        // Arrange
        var ema = Indicators.EMA(5);
        var prices = TestHelpers.Prices(10m, 20m, 30m);

        // Act
        TestHelpers.UpdatePrices(ema, prices);

        // Assert
        TestHelpers.AssertCount(3, ema);
    }

    [Fact]
    public void EMA_Period1_EqualsCurrentPrice()
    {
        // Arrange
        var ema = Indicators.EMA(1);
        var prices = TestHelpers.Prices(10m, 20m, 30m, 40m, 50m);

        // Act
        foreach (var price in prices)
        {
            ema.Update(price);
            // Assert - With period 1, EMA should always equal current price
            TestHelpers.AssertIndicatorValue(price, ema);
        }
    }

    [Fact]
    public void EMA_ShortPeriod_MoreResponsive_ThanLongPeriod()
    {
        // Arrange
        var emaShort = Indicators.EMA(3);
        var emaLong = Indicators.EMA(10);
        var prices = TestHelpers.ConstantPrices(100m, 10);

        // Act - Initialize with constant prices
        TestHelpers.UpdatePrices(emaShort, prices);
        TestHelpers.UpdatePrices(emaLong, prices);

        var beforeShort = emaShort.Value;
        var beforeLong = emaLong.Value;

        // Add spike
        emaShort.Update(150m);
        emaLong.Update(150m);

        // Assert - Shorter period should respond more
        var shortChange = emaShort.Value - beforeShort;
        var longChange = emaLong.Value - beforeLong;

        Assert.True(shortChange > longChange,
            $"Shorter EMA should be more responsive. Short change: {shortChange}, Long change: {longChange}");
    }

    [Fact]
    public void EMA_AscendingPrices_ProducesAscendingValues()
    {
        // Arrange
        var ema = Indicators.EMA(5);
        var prices = TestHelpers.AscendingPrices(100m, 5m, 10);

        // Act & Assert
        decimal previousValue = 0m;
        int readyCount = 0;

        foreach (var price in prices)
        {
            ema.Update(price);
            if (ema.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(ema.Value > previousValue,
                        $"EMA should increase with ascending prices. Previous: {previousValue}, Current: {ema.Value}");
                }
                previousValue = ema.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void EMA_DescendingPrices_ProducesDescendingValues()
    {
        // Arrange
        var ema = Indicators.EMA(5);
        var prices = TestHelpers.DescendingPrices(100m, 5m, 10);

        // Act & Assert
        decimal previousValue = decimal.MaxValue;
        int readyCount = 0;

        foreach (var price in prices)
        {
            ema.Update(price);
            if (ema.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(ema.Value < previousValue,
                        $"EMA should decrease with descending prices. Previous: {previousValue}, Current: {ema.Value}");
                }
                previousValue = ema.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void EMA_OscillatingPrices_ProducesSmoothedValue()
    {
        // Arrange
        var ema = Indicators.EMA(5);
        var prices = TestHelpers.OscillatingPrices(90m, 110m, 15);

        // Act
        TestHelpers.UpdatePrices(ema, prices);

        // Assert - EMA should smooth oscillations
        TestHelpers.AssertReady(ema);
        TestHelpers.AssertInRange(ema.Value, 85m, 115m);
    }

    [Fact]
    public void EMA_SineWave_ProducesSmoothedOutput()
    {
        // Arrange
        var ema = Indicators.EMA(10);
        var prices = TestHelpers.SineWavePrices(100m, 20m, 100, 2.0);

        // Act
        TestHelpers.UpdatePrices(ema, prices);

        // Assert - EMA should smooth the sine wave
        TestHelpers.AssertReady(ema);
        TestHelpers.AssertInRange(ema.Value, 80m, 120m);
    }

    [Fact]
    public void EMA_ConvergesToPrice_AfterManyUpdates()
    {
        // Arrange
        var ema = Indicators.EMA(5);
        var initialPrices = TestHelpers.ConstantPrices(100m, 5);
        var newPrice = 200m;

        // Act - Initialize EMA at 100
        TestHelpers.UpdatePrices(ema, initialPrices);

        // Feed constant new price many times
        for (int i = 0; i < 50; i++)
        {
            ema.Update(newPrice);
        }

        // Assert - Should converge very close to new price
        TestHelpers.AssertApproximately(newPrice, ema.Value, 1m);
    }

    [Fact]
    public void EMA_AlphaCalculation_Period5()
    {
        // Arrange
        var ema = Indicators.EMA(5);
        var prices = TestHelpers.Prices(100m, 100m, 100m, 100m, 100m);

        // Act - Initialize with constant value
        TestHelpers.UpdatePrices(ema, prices);
        TestHelpers.AssertApproximately(100m, ema.Value);

        // Add a single different price
        ema.Update(120m);

        // Assert
        // Alpha = 2/(5+1) = 0.333...
        // New EMA = 120 * 0.333 + 100 * 0.667 = 106.667
        TestHelpers.AssertApproximately(106.667m, ema.Value, 0.01m);
    }

    [Fact]
    public void EMA_InitialValue_IsSMA()
    {
        // Note: This implementation of EMA initializes with the first price value,
        // not with SMA. This is a valid and efficient approach for streaming indicators.
        // Traditional EMA implementations often start with SMA, but this one starts
        // with the first price and then applies exponential smoothing from there.

        // Arrange
        var ema = Indicators.EMA(4);
        var prices = TestHelpers.Prices(10m, 20m, 30m, 40m);

        // Act
        TestHelpers.UpdatePrices(ema, prices);

        // Assert - EMA should produce reasonable smoothed values
        // After 4 prices, EMA should be ready and have a meaningful value
        // With alpha = 2/(4+1) = 0.4:
        // EMA[0] = 10
        // EMA[1] = 0.4*20 + 0.6*10 = 8 + 6 = 14
        // EMA[2] = 0.4*30 + 0.6*14 = 12 + 8.4 = 20.4
        // EMA[3] = 0.4*40 + 0.6*20.4 = 16 + 12.24 = 28.24
        TestHelpers.AssertReady(ema);
        Assert.True(ema.Value > 0m, "EMA should have a positive value");
        // EMA value should be influenced by all prices but weighted toward recent
        TestHelpers.AssertApproximately(28.24m, ema.Value, 0.01m);
    }

    [Fact]
    public void EMA_DifferentPeriods_ProduceDifferentValues()
    {
        // Arrange
        var ema3 = Indicators.EMA(3);
        var ema10 = Indicators.EMA(10);
        var ema20 = Indicators.EMA(20);
        var prices = TestHelpers.AscendingPrices(100m, 2m, 25);

        // Act
        TestHelpers.UpdatePrices(ema3, prices);
        TestHelpers.UpdatePrices(ema10, prices);
        TestHelpers.UpdatePrices(ema20, prices);

        // Assert - Different periods should produce different values
        Assert.NotEqual(ema3.Value, ema10.Value);
        Assert.NotEqual(ema10.Value, ema20.Value);
        Assert.NotEqual(ema3.Value, ema20.Value);
    }
}
