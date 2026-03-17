using Rhodium.Primitives;
using Rhodium.Indicators;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Zero Lag Exponential Moving Average (ZLEMA) indicator.
/// ZLEMA = EMA(price + (price - price[lag])) where lag = (period-1)/2
/// Designed to eliminate lag by compensating for price momentum.
/// </summary>
public class ZLEMATests
{
    [Fact]
    public void ZLEMA_BecomesReady_AfterPeriodUpdates()
    {
        // ZLEMA needs period updates plus lookback for lag calculation
        var period = 10;
        var zlema = Indicators.ZLEMA(period);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 20);

        // Act - Update until ready
        int updateCount = 0;
        foreach (var price in prices)
        {
            zlema.Update(price);
            updateCount++;
            if (zlema.IsReady) break;
        }

        // Assert
        TestHelpers.AssertReady(zlema);
        Assert.True(updateCount >= period, $"ZLEMA should need at least {period} updates, got {updateCount}");
    }

    [Fact]
    public void ZLEMA_ResetsCorrectly()
    {
        // Arrange
        var zlema = Indicators.ZLEMA(10);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 20);

        // Act & Assert
        TestHelpers.TestReset(zlema, () => TestHelpers.UpdatePrices(zlema, prices));
    }

    [Fact]
    public void ZLEMA_ProducesConstantValue_WithConstantPrices()
    {
        // Arrange
        var zlema = Indicators.ZLEMA(10);
        var constantValue = 100m;
        var prices = TestHelpers.ConstantPrices(constantValue, 20);

        // Act
        TestHelpers.UpdatePrices(zlema, prices);

        // Assert - With constant prices, lag compensation = 0, so ZLEMA = constant
        TestHelpers.AssertIndicatorValue(constantValue, zlema, 0.01m);
    }

    [Fact]
    public void ZLEMA_HandlesZeroPrices()
    {
        // Arrange
        var zlema = Indicators.ZLEMA(10);

        // Act & Assert
        TestHelpers.TestZeroPrices(zlema, 20);
        TestHelpers.AssertIndicatorValue(0m, zlema, 0.01m);
    }

    [Fact]
    public void ZLEMA_HandlesLargePrices()
    {
        // Arrange
        var zlema = Indicators.ZLEMA(10);

        // Act & Assert
        TestHelpers.TestLargePrices(zlema);
    }

    [Fact]
    public void ZLEMA_RespondsToTrends()
    {
        // Arrange
        var zlema = Indicators.ZLEMA(10);
        var ascending = TestHelpers.AscendingPrices(100m, 1m, 25);
        var descending = TestHelpers.DescendingPrices(100m, 1m, 25);

        // Act & Assert
        TestHelpers.TestResponsiveness(zlema, ascending, descending);
    }

    [Fact]
    public void ZLEMA_CountIncrementsCorrectly()
    {
        // Arrange
        var zlema = Indicators.ZLEMA(10);
        var prices = TestHelpers.Prices(10m, 20m, 30m);

        // Act
        TestHelpers.UpdatePrices(zlema, prices);

        // Assert
        TestHelpers.AssertCount(3, zlema);
    }

    [Fact]
    public void ZLEMA_MoreResponsive_ThanEMA()
    {
        // Arrange
        var zlema = Indicators.ZLEMA(10);
        var ema = Indicators.EMA(10);
        var prices = TestHelpers.ConstantPrices(100m, 15);

        // Act - Initialize both with constant prices
        TestHelpers.UpdatePrices(zlema, prices);
        TestHelpers.UpdatePrices(ema, prices);

        var zlemaBefore = zlema.Value;
        var emaBefore = ema.Value;

        // Add a sudden spike
        zlema.Update(150m);
        ema.Update(150m);

        // Assert - ZLEMA should respond more due to lag elimination
        var zlemaChange = Math.Abs(zlema.Value - zlemaBefore);
        var emaChange = Math.Abs(ema.Value - emaBefore);

        Assert.True(zlemaChange > emaChange,
            $"ZLEMA should be more responsive. ZLEMA change: {zlemaChange}, EMA change: {emaChange}");
    }

    [Fact]
    public void ZLEMA_ReducesLag_ComparedToEMA()
    {
        // Test that ZLEMA follows trends more closely
        // Arrange
        var zlema = Indicators.ZLEMA(10);
        var ema = Indicators.EMA(10);
        var prices = TestHelpers.AscendingPrices(100m, 3m, 25);

        // Act
        TestHelpers.UpdatePrices(zlema, prices);
        TestHelpers.UpdatePrices(ema, prices);

        // Assert - ZLEMA should be closer to current price (less lag)
        var currentPrice = prices[^1];
        var zlemaDistance = Math.Abs(currentPrice - zlema.Value);
        var emaDistance = Math.Abs(currentPrice - ema.Value);

        Assert.True(zlemaDistance < emaDistance,
            $"ZLEMA should have less lag. ZLEMA distance: {zlemaDistance}, EMA distance: {emaDistance}");
    }

    [Fact]
    public void ZLEMA_AscendingPrices_ProducesAscendingValues()
    {
        // Arrange
        var zlema = Indicators.ZLEMA(10);
        var prices = TestHelpers.AscendingPrices(100m, 5m, 25);

        // Act & Assert
        decimal previousValue = 0m;
        int readyCount = 0;

        foreach (var price in prices)
        {
            zlema.Update(price);
            if (zlema.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(zlema.Value >= previousValue - 0.5m, // ZLEMA can overshoot slightly
                        $"ZLEMA should increase with ascending prices. Previous: {previousValue}, Current: {zlema.Value}");
                }
                previousValue = zlema.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void ZLEMA_DescendingPrices_ProducesDescendingValues()
    {
        // Arrange
        var zlema = Indicators.ZLEMA(10);
        var prices = TestHelpers.DescendingPrices(100m, 5m, 25);

        // Act & Assert
        decimal previousValue = decimal.MaxValue;
        int readyCount = 0;

        foreach (var price in prices)
        {
            zlema.Update(price);
            if (zlema.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(zlema.Value <= previousValue + 0.5m, // ZLEMA can overshoot slightly
                        $"ZLEMA should decrease with descending prices. Previous: {previousValue}, Current: {zlema.Value}");
                }
                previousValue = zlema.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void ZLEMA_OscillatingPrices_ProducesSmoothedValue()
    {
        // Arrange
        var zlema = Indicators.ZLEMA(10);
        var prices = TestHelpers.OscillatingPrices(90m, 110m, 30);

        // Act
        TestHelpers.UpdatePrices(zlema, prices);

        // Assert - ZLEMA should smooth while staying responsive
        TestHelpers.AssertReady(zlema);
        TestHelpers.AssertInRange(zlema.Value, 85m, 115m);
    }

    [Fact]
    public void ZLEMA_SineWave_ProducesSmoothedOutput()
    {
        // Arrange
        var zlema = Indicators.ZLEMA(10);
        var prices = TestHelpers.SineWavePrices(100m, 20m, 100, 2.0);

        // Act
        TestHelpers.UpdatePrices(zlema, prices);

        // Assert - ZLEMA should smooth the sine wave with minimal lag
        TestHelpers.AssertReady(zlema);
        TestHelpers.AssertInRange(zlema.Value, 80m, 120m);
    }

    [Fact]
    public void ZLEMA_Formula_UsesLagCompensation()
    {
        // Documents that ZLEMA compensates for lag: EMA(price + (price - price[lag]))
        // Arrange
        var zlema = Indicators.ZLEMA(10);
        var prices = TestHelpers.ConstantPrices(100m, 20);

        // Act
        TestHelpers.UpdatePrices(zlema, prices);

        // Assert - With constant prices, (price - price[lag]) = 0, so ZLEMA = EMA(100) = 100
        TestHelpers.AssertApproximately(100m, zlema.Value, 0.1m);
    }

    [Fact]
    public void ZLEMA_LagCalculation_HalfPeriod()
    {
        // Lag = (period - 1) / 2
        // For period 10, lag = 9/2 = 4.5 ≈ 4
        // For period 9, lag = 8/2 = 4
        // This test documents the lag calculation
        // Arrange
        var zlema9 = Indicators.ZLEMA(9);
        var zlema10 = Indicators.ZLEMA(10);
        var prices = TestHelpers.AscendingPrices(100m, 2m, 20);

        // Act
        TestHelpers.UpdatePrices(zlema9, prices);
        TestHelpers.UpdatePrices(zlema10, prices);

        // Assert - Both should work correctly
        TestHelpers.AssertReady(zlema9);
        TestHelpers.AssertReady(zlema10);
    }

    [Fact]
    public void ZLEMA_CanOvershoots_OnSharpTrends()
    {
        // ZLEMA's lag compensation can cause overshooting
        // Arrange
        var zlema = Indicators.ZLEMA(10);
        var ema = Indicators.EMA(10);

        // Sharp uptrend then plateau
        var prices = TestHelpers.Prices(100m, 100m, 100m, 100m, 100m, 100m,
                                        120m, 120m, 120m, 120m, 120m, 120m,
                                        120m, 120m, 120m, 120m, 120m, 120m);

        // Act
        TestHelpers.UpdatePrices(zlema, prices);
        TestHelpers.UpdatePrices(ema, prices);

        // Assert - ZLEMA might overshoot during transition
        TestHelpers.AssertReady(zlema);
        TestHelpers.AssertReady(ema);
    }

    [Fact]
    public void ZLEMA_Period1_ApproximatesCurrentPrice()
    {
        // Arrange
        var zlema = Indicators.ZLEMA(1);
        var prices = TestHelpers.Prices(10m, 20m, 30m, 40m, 50m);

        // Act
        foreach (var price in prices)
        {
            zlema.Update(price);
            if (zlema.IsReady)
            {
                // Assert - With period 1, ZLEMA should be very close to current price
                TestHelpers.AssertApproximately(price, zlema.Value, 0.5m);
            }
        }
    }

    [Fact]
    public void ZLEMA_ShortPeriod_MoreResponsive_ThanLongPeriod()
    {
        // Arrange
        var zlemaShort = Indicators.ZLEMA(5);
        var zlemaLong = Indicators.ZLEMA(15);
        var prices = TestHelpers.ConstantPrices(100m, 20);

        // Act - Initialize with constant prices
        TestHelpers.UpdatePrices(zlemaShort, prices);
        TestHelpers.UpdatePrices(zlemaLong, prices);

        var beforeShort = zlemaShort.Value;
        var beforeLong = zlemaLong.Value;

        // Add spike
        zlemaShort.Update(150m);
        zlemaLong.Update(150m);

        // Assert - Shorter period should respond more
        var shortChange = Math.Abs(zlemaShort.Value - beforeShort);
        var longChange = Math.Abs(zlemaLong.Value - beforeLong);

        Assert.True(shortChange > longChange,
            $"Shorter ZLEMA should be more responsive. Short change: {shortChange}, Long change: {longChange}");
    }

    [Fact]
    public void ZLEMA_TrendFollowing_BetterThanEMA()
    {
        // Arrange
        var zlema = Indicators.ZLEMA(10);
        var ema = Indicators.EMA(10);
        var trendPrices = TestHelpers.AscendingPrices(100m, 4m, 25);

        // Act
        TestHelpers.UpdatePrices(zlema, trendPrices);
        TestHelpers.UpdatePrices(ema, trendPrices);

        // Assert - ZLEMA should track the trend more closely
        var currentPrice = trendPrices[^1];
        var zlemaLag = currentPrice - zlema.Value;
        var emaLag = currentPrice - ema.Value;

        Assert.True(zlemaLag < emaLag,
            $"ZLEMA should have less lag in trending market. ZLEMA lag: {zlemaLag}, EMA lag: {emaLag}");
    }

    [Fact]
    public void ZLEMA_DifferentPeriods_ProduceDifferentValues()
    {
        // Arrange
        var zlema5 = Indicators.ZLEMA(5);
        var zlema10 = Indicators.ZLEMA(10);
        var zlema20 = Indicators.ZLEMA(20);
        var prices = TestHelpers.AscendingPrices(100m, 2m, 30);

        // Act
        TestHelpers.UpdatePrices(zlema5, prices);
        TestHelpers.UpdatePrices(zlema10, prices);
        TestHelpers.UpdatePrices(zlema20, prices);

        // Assert - Different periods should produce different values
        Assert.NotEqual(zlema5.Value, zlema10.Value);
        Assert.NotEqual(zlema10.Value, zlema20.Value);
        Assert.NotEqual(zlema5.Value, zlema20.Value);
    }

    [Fact]
    public void ZLEMA_MomentumCompensation_Works()
    {
        // ZLEMA should add momentum compensation in trending markets
        // Arrange
        var zlema = Indicators.ZLEMA(10);
        var ema = Indicators.EMA(10);

        // Strong consistent trend
        var strongTrend = TestHelpers.AscendingPrices(100m, 5m, 20);

        // Act
        TestHelpers.UpdatePrices(zlema, strongTrend);
        TestHelpers.UpdatePrices(ema, strongTrend);

        // Assert - ZLEMA should be ahead of EMA due to momentum compensation
        Assert.True(zlema.Value > ema.Value,
            $"ZLEMA should be higher than EMA in uptrend. ZLEMA: {zlema.Value}, EMA: {ema.Value}");
    }

    [Fact]
    public void ZLEMA_StableAfterConvergence()
    {
        // Arrange
        var zlema = Indicators.ZLEMA(10);
        var constantValue = 100m;
        var prices = TestHelpers.ConstantPrices(constantValue, 30);

        // Act
        TestHelpers.UpdatePrices(zlema, prices);

        // Assert - Should converge to constant value
        TestHelpers.AssertApproximately(constantValue, zlema.Value, 0.5m);
    }

    [Fact]
    public void ZLEMA_CompareTo_DEMA()
    {
        // Both ZLEMA and DEMA aim to reduce lag but use different methods
        // Arrange
        var zlema = Indicators.ZLEMA(10);
        var dema = Indicators.DEMA(10);
        var prices = TestHelpers.AscendingPrices(100m, 3m, 30);

        // Act
        TestHelpers.UpdatePrices(zlema, prices);
        TestHelpers.UpdatePrices(dema, prices);

        // Assert - Both should reduce lag effectively
        TestHelpers.AssertReady(zlema);
        TestHelpers.AssertReady(dema);

        var currentPrice = prices[^1];
        var zlemaDistance = Math.Abs(currentPrice - zlema.Value);
        var demaDistance = Math.Abs(currentPrice - dema.Value);

        // Both should be close to current price (low lag)
        Assert.True(zlemaDistance < 20m && demaDistance < 20m,
            $"Both should have low lag. ZLEMA: {zlemaDistance}, DEMA: {demaDistance}");
    }

    [Fact]
    public void ZLEMA_BetterFor_StrongTrends()
    {
        // ZLEMA's momentum compensation works best in trends
        // Arrange
        var zlema = Indicators.ZLEMA(10);
        var ema = Indicators.EMA(10);

        // Very strong trend
        var prices = TestHelpers.AscendingPrices(100m, 10m, 20);

        // Act
        TestHelpers.UpdatePrices(zlema, prices);
        TestHelpers.UpdatePrices(ema, prices);

        // Assert - ZLEMA should significantly outperform EMA
        var currentPrice = prices[^1];
        var zlemaLag = currentPrice - zlema.Value;
        var emaLag = currentPrice - ema.Value;

        Assert.True(zlemaLag < emaLag * 0.8m, // At least 20% less lag
            $"ZLEMA should significantly reduce lag. ZLEMA lag: {zlemaLag}, EMA lag: {emaLag}");
    }
}
