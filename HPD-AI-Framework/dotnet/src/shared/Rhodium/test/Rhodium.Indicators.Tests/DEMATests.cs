using Rhodium.Primitives;
using Rhodium.Indicators;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Double Exponential Moving Average (DEMA) indicator.
/// DEMA = 2*EMA - EMA(EMA) - reduces lag compared to standard EMA.
/// </summary>
public class DEMATests
{
    [Fact]
    public void DEMA_BecomesReady_AfterPeriodUpdates()
    {
        // DEMA needs period updates for first EMA, then another period for second EMA
        // So it becomes ready after approximately 2*period - 1 updates
        var period = 5;
        var dema = Indicators.DEMA(period);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 20);

        // Act - Update until ready
        int updateCount = 0;
        foreach (var price in prices)
        {
            dema.Update(price);
            updateCount++;
            if (dema.IsReady) break;
        }

        // Assert - Should be ready after around 2*period updates
        TestHelpers.AssertReady(dema);
        Assert.True(updateCount >= period, $"DEMA should need at least {period} updates, got {updateCount}");
    }

    [Fact]
    public void DEMA_ResetsCorrectly()
    {
        // Arrange
        var dema = Indicators.DEMA(5);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 20);

        // Act & Assert
        TestHelpers.TestReset(dema, () => TestHelpers.UpdatePrices(dema, prices));
    }

    [Fact]
    public void DEMA_ProducesConstantValue_WithConstantPrices()
    {
        // Arrange
        var dema = Indicators.DEMA(5);
        var constantValue = 100m;
        var prices = TestHelpers.ConstantPrices(constantValue, 20);

        // Act
        TestHelpers.UpdatePrices(dema, prices);

        // Assert
        TestHelpers.AssertIndicatorValue(constantValue, dema, 0.01m);
    }

    [Fact]
    public void DEMA_HandlesZeroPrices()
    {
        // Arrange
        var dema = Indicators.DEMA(5);

        // Act & Assert
        TestHelpers.TestZeroPrices(dema, 20);
        TestHelpers.AssertIndicatorValue(0m, dema, 0.01m);
    }

    [Fact]
    public void DEMA_HandlesLargePrices()
    {
        // Arrange
        var dema = Indicators.DEMA(5);

        // Act & Assert
        TestHelpers.TestLargePrices(dema);
    }

    [Fact]
    public void DEMA_RespondsToTrends()
    {
        // Arrange
        var dema = Indicators.DEMA(5);
        var ascending = TestHelpers.AscendingPrices(100m, 1m, 20);
        var descending = TestHelpers.DescendingPrices(100m, 1m, 20);

        // Act & Assert
        TestHelpers.TestResponsiveness(dema, ascending, descending);
    }

    [Fact]
    public void DEMA_CountIncrementsCorrectly()
    {
        // Arrange
        var dema = Indicators.DEMA(5);
        var prices = TestHelpers.Prices(10m, 20m, 30m);

        // Act
        TestHelpers.UpdatePrices(dema, prices);

        // Assert
        TestHelpers.AssertCount(3, dema);
    }

    [Fact]
    public void DEMA_MoreResponsive_ThanEMA()
    {
        // Arrange
        var dema = Indicators.DEMA(10);
        var ema = Indicators.EMA(10);
        var prices = TestHelpers.ConstantPrices(100m, 20);

        // Act - Initialize both with constant prices
        TestHelpers.UpdatePrices(dema, prices);
        TestHelpers.UpdatePrices(ema, prices);

        var demaBeforeSpike = dema.Value;
        var emaBeforeSpike = ema.Value;

        // Add a sudden spike
        dema.Update(150m);
        ema.Update(150m);

        // Assert - DEMA should respond more due to lag reduction
        var demaChange = Math.Abs(dema.Value - demaBeforeSpike);
        var emaChange = Math.Abs(ema.Value - emaBeforeSpike);

        Assert.True(demaChange > emaChange,
            $"DEMA should be more responsive than EMA. DEMA change: {demaChange}, EMA change: {emaChange}");
    }

    [Fact]
    public void DEMA_ReducesLag_ComparedToEMA()
    {
        // Test that DEMA follows trends more closely
        // Arrange
        var dema = Indicators.DEMA(10);
        var ema = Indicators.EMA(10);
        var prices = TestHelpers.AscendingPrices(100m, 2m, 25);

        // Act
        TestHelpers.UpdatePrices(dema, prices);
        TestHelpers.UpdatePrices(ema, prices);

        // Assert - DEMA should be closer to current price (less lag)
        var currentPrice = prices[^1];
        var demaDistance = Math.Abs(currentPrice - dema.Value);
        var emaDistance = Math.Abs(currentPrice - ema.Value);

        Assert.True(demaDistance < emaDistance,
            $"DEMA should have less lag. DEMA distance: {demaDistance}, EMA distance: {emaDistance}");
    }

    [Fact]
    public void DEMA_AscendingPrices_ProducesAscendingValues()
    {
        // Arrange
        var dema = Indicators.DEMA(5);
        var prices = TestHelpers.AscendingPrices(100m, 5m, 20);

        // Act & Assert
        decimal previousValue = 0m;
        int readyCount = 0;

        foreach (var price in prices)
        {
            dema.Update(price);
            if (dema.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(dema.Value >= previousValue - 0.01m, // Allow tiny rounding
                        $"DEMA should increase with ascending prices. Previous: {previousValue}, Current: {dema.Value}");
                }
                previousValue = dema.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void DEMA_DescendingPrices_ProducesDescendingValues()
    {
        // Arrange
        var dema = Indicators.DEMA(5);
        var prices = TestHelpers.DescendingPrices(100m, 5m, 20);

        // Act & Assert
        decimal previousValue = decimal.MaxValue;
        int readyCount = 0;

        foreach (var price in prices)
        {
            dema.Update(price);
            if (dema.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(dema.Value <= previousValue + 0.01m, // Allow tiny rounding
                        $"DEMA should decrease with descending prices. Previous: {previousValue}, Current: {dema.Value}");
                }
                previousValue = dema.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void DEMA_OscillatingPrices_ProducesSmoothedValue()
    {
        // Arrange
        var dema = Indicators.DEMA(5);
        var prices = TestHelpers.OscillatingPrices(90m, 110m, 25);

        // Act
        TestHelpers.UpdatePrices(dema, prices);

        // Assert - DEMA should smooth oscillations while staying responsive
        TestHelpers.AssertReady(dema);
        TestHelpers.AssertInRange(dema.Value, 85m, 115m);
    }

    [Fact]
    public void DEMA_SineWave_ProducesSmoothedOutput()
    {
        // Arrange
        var dema = Indicators.DEMA(10);
        var prices = TestHelpers.SineWavePrices(100m, 20m, 100, 2.0);

        // Act
        TestHelpers.UpdatePrices(dema, prices);

        // Assert - DEMA should smooth the sine wave with reduced lag
        TestHelpers.AssertReady(dema);
        TestHelpers.AssertInRange(dema.Value, 80m, 120m);
    }

    [Fact]
    public void DEMA_Formula_TwoEmaMinusEmaOfEma()
    {
        // This documents that DEMA = 2*EMA(n) - EMA(EMA(n))
        // Arrange
        var dema = Indicators.DEMA(5);
        var prices = TestHelpers.ConstantPrices(100m, 20);

        // Act
        TestHelpers.UpdatePrices(dema, prices);

        // Assert - With constant prices, formula should produce constant value
        // 2*100 - 100 = 100
        TestHelpers.AssertApproximately(100m, dema.Value, 0.1m);
    }

    [Fact]
    public void DEMA_CanOvershoots_OnSharpTrends()
    {
        // DEMA's reduced lag can cause overshooting
        // Arrange
        var dema = Indicators.DEMA(5);
        var ema = Indicators.EMA(5);

        // Sharp uptrend
        var prices = TestHelpers.Prices(100m, 100m, 100m, 100m, 100m, 150m, 150m, 150m, 150m, 150m);

        // Act
        TestHelpers.UpdatePrices(dema, prices);
        TestHelpers.UpdatePrices(ema, prices);

        // Assert - DEMA might overshoot above the constant target
        // This is expected behavior due to the 2*EMA formula
        TestHelpers.AssertReady(dema);
        TestHelpers.AssertReady(ema);
    }

    [Fact]
    public void DEMA_Period1_ApproximatesCurrentPrice()
    {
        // Arrange
        var dema = Indicators.DEMA(1);
        var prices = TestHelpers.Prices(10m, 20m, 30m, 40m, 50m);

        // Act
        foreach (var price in prices)
        {
            dema.Update(price);
            if (dema.IsReady)
            {
                // Assert - With period 1, DEMA should be very close to current price
                TestHelpers.AssertApproximately(price, dema.Value, 0.1m);
            }
        }
    }

    [Fact]
    public void DEMA_ShortPeriod_MoreResponsive_ThanLongPeriod()
    {
        // Arrange
        var demaShort = Indicators.DEMA(3);
        var demaLong = Indicators.DEMA(10);
        var prices = TestHelpers.ConstantPrices(100m, 20);

        // Act - Initialize with constant prices
        TestHelpers.UpdatePrices(demaShort, prices);
        TestHelpers.UpdatePrices(demaLong, prices);

        var beforeShort = demaShort.Value;
        var beforeLong = demaLong.Value;

        // Add spike
        demaShort.Update(150m);
        demaLong.Update(150m);

        // Assert - Shorter period should respond more
        var shortChange = Math.Abs(demaShort.Value - beforeShort);
        var longChange = Math.Abs(demaLong.Value - beforeLong);

        Assert.True(shortChange > longChange,
            $"Shorter DEMA should be more responsive. Short change: {shortChange}, Long change: {longChange}");
    }

    [Fact]
    public void DEMA_TrendFollowing_BetterThanEMA()
    {
        // Arrange
        var dema = Indicators.DEMA(8);
        var ema = Indicators.EMA(8);

        // Strong trend: 100, 105, 110, 115, 120, 125, 130
        var trendPrices = TestHelpers.AscendingPrices(100m, 5m, 15);

        // Act
        TestHelpers.UpdatePrices(dema, trendPrices);
        TestHelpers.UpdatePrices(ema, trendPrices);

        // Assert - DEMA should track the trend more closely
        var currentPrice = trendPrices[^1];
        var demaLag = currentPrice - dema.Value;
        var emaLag = currentPrice - ema.Value;

        Assert.True(demaLag < emaLag,
            $"DEMA should have less lag in trending market. DEMA lag: {demaLag}, EMA lag: {emaLag}");
    }

    [Fact]
    public void DEMA_DifferentPeriods_ProduceDifferentValues()
    {
        // Arrange
        var dema3 = Indicators.DEMA(3);
        var dema10 = Indicators.DEMA(10);
        var dema20 = Indicators.DEMA(20);
        var prices = TestHelpers.AscendingPrices(100m, 2m, 50);

        // Act
        TestHelpers.UpdatePrices(dema3, prices);
        TestHelpers.UpdatePrices(dema10, prices);
        TestHelpers.UpdatePrices(dema20, prices);

        // Assert - Different periods should produce different values
        Assert.NotEqual(dema3.Value, dema10.Value);
        Assert.NotEqual(dema10.Value, dema20.Value);
        Assert.NotEqual(dema3.Value, dema20.Value);
    }

    [Fact]
    public void DEMA_StableAfterConvergence()
    {
        // Arrange
        var dema = Indicators.DEMA(5);
        var constantValue = 100m;
        var prices = TestHelpers.ConstantPrices(constantValue, 30);

        // Act
        TestHelpers.UpdatePrices(dema, prices);

        // Assert - Should converge to constant value
        TestHelpers.AssertApproximately(constantValue, dema.Value, 0.5m);
    }
}
