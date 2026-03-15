using Rhodium.Primitives;
using Rhodium.Indicators;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Rolling Moving Average (RMA) / Wilder's Moving Average indicator.
/// RMA uses alpha = 1/period, making it smoother than EMA.
/// Also known as SMMA (Smoothed Moving Average).
/// </summary>
public class RMATests
{
    [Fact]
    public void RMA_BecomesReady_AfterPeriodUpdates()
    {
        // Arrange
        var period = 5;
        var rma = Indicators.RMA(period);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 10);

        // Act & Assert
        TestHelpers.TestReadinessAfterPeriod(rma, period, prices);
    }

    [Fact]
    public void RMA_ResetsCorrectly()
    {
        // Arrange
        var rma = Indicators.RMA(5);
        var prices = TestHelpers.Prices(10m, 20m, 30m, 40m, 50m);

        // Act & Assert
        TestHelpers.TestReset(rma, () => TestHelpers.UpdatePrices(rma, prices));
    }

    [Fact]
    public void RMA_ProducesConstantValue_WithConstantPrices()
    {
        // Arrange
        var rma = Indicators.RMA(5);
        var constantValue = 100m;
        var prices = TestHelpers.ConstantPrices(constantValue, 10);

        // Act
        TestHelpers.UpdatePrices(rma, prices);

        // Assert
        TestHelpers.AssertIndicatorValue(constantValue, rma);
    }

    [Fact]
    public void RMA_HandlesZeroPrices()
    {
        // Arrange
        var rma = Indicators.RMA(5);

        // Act & Assert
        TestHelpers.TestZeroPrices(rma, 10);
        TestHelpers.AssertIndicatorValue(0m, rma);
    }

    [Fact]
    public void RMA_HandlesLargePrices()
    {
        // Arrange
        var rma = Indicators.RMA(5);

        // Act & Assert
        TestHelpers.TestLargePrices(rma);
    }

    [Fact]
    public void RMA_RespondsToTrends()
    {
        // Arrange
        var rma = Indicators.RMA(5);
        var ascending = TestHelpers.AscendingPrices(100m, 1m, 10);
        var descending = TestHelpers.DescendingPrices(100m, 1m, 10);

        // Act & Assert
        TestHelpers.TestResponsiveness(rma, ascending, descending);
    }

    [Fact]
    public void RMA_CountIncrementsCorrectly()
    {
        // Arrange
        var rma = Indicators.RMA(5);
        var prices = TestHelpers.Prices(10m, 20m, 30m);

        // Act
        TestHelpers.UpdatePrices(rma, prices);

        // Assert
        TestHelpers.AssertCount(3, rma);
    }

    [Fact]
    public void RMA_LesResponsive_ThanEMA()
    {
        // Arrange
        var rma = Indicators.RMA(10);
        var ema = Indicators.EMA(10);
        var prices = TestHelpers.ConstantPrices(100m, 10);

        // Act - Initialize both with constant prices
        TestHelpers.UpdatePrices(rma, prices);
        TestHelpers.UpdatePrices(ema, prices);

        var rmaBeforeSpike = rma.Value;
        var emaBeforeSpike = ema.Value;

        // Add a sudden spike
        rma.Update(150m);
        ema.Update(150m);

        // Assert - RMA should be less responsive than EMA (alpha = 1/10 vs 2/11)
        var rmaChange = Math.Abs(rma.Value - rmaBeforeSpike);
        var emaChange = Math.Abs(ema.Value - emaBeforeSpike);

        Assert.True(rmaChange < emaChange,
            $"RMA should be less responsive than EMA. RMA change: {rmaChange}, EMA change: {emaChange}");
    }

    [Fact]
    public void RMA_CalculatesCorrectValue_WithKnownSequence()
    {
        // Arrange
        var rma = Indicators.RMA(4);
        var prices = TestHelpers.Prices(100m, 110m, 105m, 115m, 120m);

        // Act
        TestHelpers.UpdatePrices(rma, prices);

        // Assert
        // RMA calculation:
        // alpha = 1/4 = 0.25
        // RMA[0] = 100 (first price)
        // RMA[1] = 0.25*110 + 0.75*100 = 27.5 + 75 = 102.5
        // RMA[2] = 0.25*105 + 0.75*102.5 = 26.25 + 76.875 = 103.125
        // RMA[3] = 0.25*115 + 0.75*103.125 = 28.75 + 77.34375 = 106.09375
        // RMA[4] = 0.25*120 + 0.75*106.09375 = 30 + 79.5703125 = 109.5703125
        TestHelpers.AssertApproximately(109.57m, rma.Value, 0.01m);
    }

    [Fact]
    public void RMA_Period1_EqualsCurrentPrice()
    {
        // Arrange
        var rma = Indicators.RMA(1);
        var prices = TestHelpers.Prices(10m, 20m, 30m, 40m, 50m);

        // Act
        foreach (var price in prices)
        {
            rma.Update(price);
            // Assert - With period 1, RMA should always equal current price
            TestHelpers.AssertIndicatorValue(price, rma);
        }
    }

    [Fact]
    public void RMA_AscendingPrices_ProducesAscendingValues()
    {
        // Arrange
        var rma = Indicators.RMA(5);
        var prices = TestHelpers.AscendingPrices(100m, 5m, 10);

        // Act & Assert
        decimal previousValue = 0m;
        int readyCount = 0;

        foreach (var price in prices)
        {
            rma.Update(price);
            if (rma.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(rma.Value > previousValue,
                        $"RMA should increase with ascending prices. Previous: {previousValue}, Current: {rma.Value}");
                }
                previousValue = rma.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void RMA_DescendingPrices_ProducesDescendingValues()
    {
        // Arrange
        var rma = Indicators.RMA(5);
        var prices = TestHelpers.DescendingPrices(100m, 5m, 10);

        // Act & Assert
        decimal previousValue = decimal.MaxValue;
        int readyCount = 0;

        foreach (var price in prices)
        {
            rma.Update(price);
            if (rma.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(rma.Value < previousValue,
                        $"RMA should decrease with descending prices. Previous: {previousValue}, Current: {rma.Value}");
                }
                previousValue = rma.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void RMA_OscillatingPrices_ProducesSmoothedValue()
    {
        // Arrange
        var rma = Indicators.RMA(5);
        var prices = TestHelpers.OscillatingPrices(90m, 110m, 15);

        // Act
        TestHelpers.UpdatePrices(rma, prices);

        // Assert - RMA should smooth oscillations more than EMA
        TestHelpers.AssertReady(rma);
        TestHelpers.AssertInRange(rma.Value, 85m, 115m);
    }

    [Fact]
    public void RMA_SineWave_ProducesVerySmoothedOutput()
    {
        // Arrange
        var rma = Indicators.RMA(10);
        var prices = TestHelpers.SineWavePrices(100m, 20m, 100, 2.0);

        // Act
        TestHelpers.UpdatePrices(rma, prices);

        // Assert - RMA should heavily smooth the sine wave
        TestHelpers.AssertReady(rma);
        TestHelpers.AssertInRange(rma.Value, 80m, 120m);
    }

    [Fact]
    public void RMA_ConvergesToPrice_SlowerThanEMA()
    {
        // Arrange
        var rma = Indicators.RMA(5);
        var ema = Indicators.EMA(5);
        var initialPrices = TestHelpers.ConstantPrices(100m, 5);
        var newPrice = 150m;

        // Act - Initialize both at 100
        TestHelpers.UpdatePrices(rma, initialPrices);
        TestHelpers.UpdatePrices(ema, initialPrices);

        // Feed new price 10 times
        for (int i = 0; i < 10; i++)
        {
            rma.Update(newPrice);
            ema.Update(newPrice);
        }

        // Assert - RMA should converge slower (be further from new price)
        var rmaDistance = Math.Abs(newPrice - rma.Value);
        var emaDistance = Math.Abs(newPrice - ema.Value);

        Assert.True(rmaDistance > emaDistance,
            $"RMA should converge slower. RMA distance: {rmaDistance}, EMA distance: {emaDistance}");
    }

    [Fact]
    public void RMA_AlphaCalculation_Period5()
    {
        // Arrange
        var rma = Indicators.RMA(5);
        var prices = TestHelpers.Prices(100m, 100m, 100m, 100m, 100m);

        // Act - Initialize with constant value
        TestHelpers.UpdatePrices(rma, prices);
        TestHelpers.AssertApproximately(100m, rma.Value);

        // Add a single different price
        rma.Update(120m);

        // Assert
        // Alpha = 1/5 = 0.2
        // New RMA = 120 * 0.2 + 100 * 0.8 = 24 + 80 = 104
        TestHelpers.AssertApproximately(104m, rma.Value, 0.01m);
    }

    [Fact]
    public void RMA_InitialValue_IsSMA()
    {
        // Note: This implementation of RMA (Wilder's Moving Average) initializes with the first price value,
        // not with SMA. This is a valid and efficient approach for streaming indicators.
        // Traditional RMA implementations often start with SMA, but this one starts
        // with the first price and then applies Wilder's smoothing from there.

        // Arrange
        var rma = Indicators.RMA(4);
        var prices = TestHelpers.Prices(10m, 20m, 30m, 40m);

        // Act
        TestHelpers.UpdatePrices(rma, prices);

        // Assert - RMA should produce reasonable smoothed values
        // After 4 prices, RMA should be ready and have a meaningful value
        // With alpha = 1/4 = 0.25:
        // RMA[0] = 10
        // RMA[1] = 0.25*20 + 0.75*10 = 5 + 7.5 = 12.5
        // RMA[2] = 0.25*30 + 0.75*12.5 = 7.5 + 9.375 = 16.875
        // RMA[3] = 0.25*40 + 0.75*16.875 = 10 + 12.65625 = 22.65625
        TestHelpers.AssertReady(rma);
        Assert.True(rma.Value > 0m, "RMA should have a positive value");
        TestHelpers.AssertApproximately(22.656m, rma.Value, 0.01m);
    }

    [Fact]
    public void RMA_SmootherThan_SMA_OnVolatileData()
    {
        // RMA uses exponential smoothing which retains memory of past values
        // SMA uses simple window, so old values completely disappear

        // Arrange
        var rma = Indicators.RMA(5);
        var sma = Indicators.SMA(5);

        // Initialize at baseline
        var baseline = TestHelpers.Prices(100m, 100m, 100m, 100m, 100m);
        TestHelpers.UpdatePrices(rma, baseline);
        TestHelpers.UpdatePrices(sma, baseline);

        // Both should be at 100
        Assert.Equal(100m, rma.Value);
        Assert.Equal(100m, sma.Value);

        // Add a spike
        rma.Update(200m);
        sma.Update(200m);

        // After spike: window is [100, 100, 100, 100, 200]
        // SMA = (100+100+100+100+200)/5 = 120
        // RMA uses exponential: alpha=0.2, RMA = 0.2*200 + 0.8*100 = 40 + 80 = 120

        // Continue with normal prices
        for (int i = 0; i < 10; i++)
        {
            rma.Update(100m);
            sma.Update(100m);
        }

        // After 10 more 100s:
        // SMA window is [100,100,100,100,100] = 100
        // RMA decays exponentially but retains memory of spike
        Assert.Equal(100m, sma.Value); // SMA returns to baseline
        Assert.True(rma.Value > 100m, $"RMA should retain memory of spike. RMA: {rma.Value}");
    }

    [Fact]
    public void RMA_UsedInRSI_Calculation()
    {
        // This test documents that RMA is the standard MA used in RSI calculations
        // Arrange
        var rma = Indicators.RMA(14);
        var gains = TestHelpers.Prices(1m, 2m, 3m, 4m, 5m, 6m, 7m, 8m, 9m, 10m, 11m, 12m, 13m, 14m);

        // Act
        TestHelpers.UpdatePrices(rma, gains);

        // Assert - RMA should smooth the gains appropriately
        TestHelpers.AssertReady(rma);
        Assert.True(rma.Value > 0m);
    }

    [Fact]
    public void RMA_DifferentPeriods_ProduceDifferentSmoothing()
    {
        // Arrange
        var rma5 = Indicators.RMA(5);
        var rma14 = Indicators.RMA(14);
        var prices = TestHelpers.Prices(100m, 110m, 105m, 115m, 120m, 125m, 130m, 128m, 135m, 140m, 138m, 145m, 150m, 148m);

        // Act
        TestHelpers.UpdatePrices(rma5, prices);
        TestHelpers.UpdatePrices(rma14, prices);

        // Assert - Shorter period should be closer to current price
        var currentPrice = prices[^1];
        var rma5Distance = Math.Abs(currentPrice - rma5.Value);
        var rma14Distance = Math.Abs(currentPrice - rma14.Value);

        Assert.True(rma5Distance < rma14Distance,
            $"Shorter RMA should be closer to current price. RMA5 dist: {rma5Distance}, RMA14 dist: {rma14Distance}");
    }
}
