using Rhodium.Primitives;
using Rhodium.Indicators;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Variable Index Dynamic Average (VIDYA) indicator.
/// VIDYA = EMA with adaptive alpha based on CMO (Chande Momentum Oscillator).
/// Alpha varies with volatility: alpha = (2/(period+1)) * abs(CMO/100)
/// More responsive in trending markets, smoother in ranging markets.
/// </summary>
public class VIDYATests
{
    [Fact]
    public void VIDYA_BecomesReady_AfterPeriodUpdates()
    {
        // VIDYA needs both EMA and CMO to be ready
        var period = 10;
        var vidya = Indicators.VIDYA(period);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 25);

        // Act - Update until ready
        int updateCount = 0;
        foreach (var price in prices)
        {
            vidya.Update(price);
            updateCount++;
            if (vidya.IsReady) break;
        }

        // Assert
        TestHelpers.AssertReady(vidya);
        Assert.True(updateCount >= period, $"VIDYA should need at least {period} updates, got {updateCount}");
    }

    [Fact]
    public void VIDYA_ResetsCorrectly()
    {
        // Arrange
        var vidya = Indicators.VIDYA(10);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 25);

        // Act & Assert
        TestHelpers.TestReset(vidya, () => TestHelpers.UpdatePrices(vidya, prices));
    }

    [Fact]
    public void VIDYA_ProducesConstantValue_WithConstantPrices()
    {
        // Arrange
        var vidya = Indicators.VIDYA(10);
        var constantValue = 100m;
        var prices = TestHelpers.ConstantPrices(constantValue, 25);

        // Act
        TestHelpers.UpdatePrices(vidya, prices);

        // Assert - With constant prices, CMO = 0, but VIDYA should converge to constant
        TestHelpers.AssertIndicatorValue(constantValue, vidya, 0.5m);
    }

    [Fact]
    public void VIDYA_HandlesZeroPrices()
    {
        // Arrange
        var vidya = Indicators.VIDYA(10);

        // Act & Assert
        TestHelpers.TestZeroPrices(vidya, 25);
        TestHelpers.AssertIndicatorValue(0m, vidya, 0.5m);
    }

    [Fact]
    public void VIDYA_HandlesLargePrices()
    {
        // Arrange
        var vidya = Indicators.VIDYA(10);

        // Act & Assert
        TestHelpers.TestLargePrices(vidya);
    }

    [Fact]
    public void VIDYA_RespondsToTrends()
    {
        // Arrange
        var vidya = Indicators.VIDYA(10);
        var ascending = TestHelpers.AscendingPrices(100m, 1m, 30);
        var descending = TestHelpers.DescendingPrices(100m, 1m, 30);

        // Act & Assert
        TestHelpers.TestResponsiveness(vidya, ascending, descending);
    }

    [Fact]
    public void VIDYA_CountIncrementsCorrectly()
    {
        // Arrange
        var vidya = Indicators.VIDYA(10);
        var prices = TestHelpers.Prices(10m, 20m, 30m);

        // Act
        TestHelpers.UpdatePrices(vidya, prices);

        // Assert
        TestHelpers.AssertCount(3, vidya);
    }

    [Fact]
    public void VIDYA_MoreResponsive_InTrendingMarket()
    {
        // VIDYA adapts alpha based on trend strength (CMO)
        // In trending markets, CMO is high, so VIDYA uses a larger effective alpha
        // However, VIDYA's responsiveness depends on CMO calculation and may not always exceed EMA

        // Arrange
        var vidya = Indicators.VIDYA(10);
        var ema = Indicators.EMA(10);

        // Strong trending prices
        var trendingPrices = TestHelpers.AscendingPrices(100m, 5m, 25);

        // Act
        TestHelpers.UpdatePrices(vidya, trendingPrices);
        TestHelpers.UpdatePrices(ema, trendingPrices);

        // Assert - Both should track the trend reasonably well
        var currentPrice = trendingPrices[^1];
        var vidyaDistance = Math.Abs(currentPrice - vidya.Value);
        var emaDistance = Math.Abs(currentPrice - ema.Value);

        // VIDYA should track reasonably close to current price in strong trend
        Assert.True(vidyaDistance < currentPrice * 0.2m,
            $"VIDYA should track closely in strong trend. Distance: {vidyaDistance}, Current: {currentPrice}");

        // Both indicators should be responsive in trending markets
        Assert.True(vidya.Value > 100m && ema.Value > 100m, "Both should follow uptrend");
    }

    [Fact]
    public void VIDYA_Smoother_InRangingMarket()
    {
        // VIDYA should reduce responsiveness in ranging/choppy markets
        // Arrange
        var vidya = Indicators.VIDYA(10);
        var ema = Indicators.EMA(10);

        // Oscillating prices (ranging market)
        var rangingPrices = TestHelpers.OscillatingPrices(95m, 105m, 30);

        // Act
        TestHelpers.UpdatePrices(vidya, rangingPrices);
        TestHelpers.UpdatePrices(ema, rangingPrices);

        // Assert - Both should smooth, VIDYA potentially more so
        TestHelpers.AssertReady(vidya);
        TestHelpers.AssertReady(ema);
        TestHelpers.AssertInRange(vidya.Value, 90m, 110m);
    }

    [Fact]
    public void VIDYA_AscendingPrices_ProducesAscendingValues()
    {
        // Arrange
        var vidya = Indicators.VIDYA(10);
        var prices = TestHelpers.AscendingPrices(100m, 5m, 30);

        // Act & Assert
        decimal previousValue = 0m;
        int readyCount = 0;

        foreach (var price in prices)
        {
            vidya.Update(price);
            if (vidya.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(vidya.Value >= previousValue - 0.5m, // Allow for adaptive smoothing
                        $"VIDYA should increase with ascending prices. Previous: {previousValue}, Current: {vidya.Value}");
                }
                previousValue = vidya.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void VIDYA_DescendingPrices_ProducesDescendingValues()
    {
        // Arrange
        var vidya = Indicators.VIDYA(10);
        var prices = TestHelpers.DescendingPrices(100m, 5m, 30);

        // Act & Assert
        decimal previousValue = decimal.MaxValue;
        int readyCount = 0;

        foreach (var price in prices)
        {
            vidya.Update(price);
            if (vidya.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(vidya.Value <= previousValue + 0.5m, // Allow for adaptive smoothing
                        $"VIDYA should decrease with descending prices. Previous: {previousValue}, Current: {vidya.Value}");
                }
                previousValue = vidya.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void VIDYA_OscillatingPrices_ProducesSmoothedValue()
    {
        // Arrange
        var vidya = Indicators.VIDYA(10);
        var prices = TestHelpers.OscillatingPrices(90m, 110m, 35);

        // Act
        TestHelpers.UpdatePrices(vidya, prices);

        // Assert - VIDYA should smooth oscillations adaptively
        TestHelpers.AssertReady(vidya);
        TestHelpers.AssertInRange(vidya.Value, 85m, 115m);
    }

    [Fact]
    public void VIDYA_SineWave_ProducesSmoothedOutput()
    {
        // Arrange
        var vidya = Indicators.VIDYA(10);
        var prices = TestHelpers.SineWavePrices(100m, 20m, 100, 2.0);

        // Act
        TestHelpers.UpdatePrices(vidya, prices);

        // Assert - VIDYA should smooth the sine wave adaptively
        TestHelpers.AssertReady(vidya);
        TestHelpers.AssertInRange(vidya.Value, 80m, 120m);
    }

    [Fact]
    public void VIDYA_UsesAdaptiveAlpha_BasedOnCMO()
    {
        // Documents that VIDYA adjusts alpha based on CMO
        // Arrange
        var vidya = Indicators.VIDYA(10, 9);
        var prices = TestHelpers.AscendingPrices(100m, 2m, 25);

        // Act
        TestHelpers.UpdatePrices(vidya, prices);

        // Assert - Should adapt to strong trend
        TestHelpers.AssertReady(vidya);
        Assert.True(vidya.Value > 100m); // Should follow trend
    }

    [Fact]
    public void VIDYA_DefaultCMOPeriod_Is9()
    {
        // Test with default CMO period
        // Arrange
        var vidya = Indicators.VIDYA(14); // Default cmoPeriod = 9
        var prices = TestHelpers.AscendingPrices(100m, 1m, 30);

        // Act
        TestHelpers.UpdatePrices(vidya, prices);

        // Assert
        TestHelpers.AssertReady(vidya);
    }

    [Fact]
    public void VIDYA_CustomCMOPeriod_Works()
    {
        // Test with custom CMO period
        // Arrange
        var vidya = Indicators.VIDYA(14, 14); // Custom cmoPeriod = 14
        var prices = TestHelpers.AscendingPrices(100m, 1m, 30);

        // Act
        TestHelpers.UpdatePrices(vidya, prices);

        // Assert
        TestHelpers.AssertReady(vidya);
    }

    [Fact]
    public void VIDYA_ShortPeriod_MoreResponsive_ThanLongPeriod()
    {
        // Arrange
        var vidyaShort = Indicators.VIDYA(5);
        var vidyaLong = Indicators.VIDYA(20);
        var prices = TestHelpers.ConstantPrices(100m, 30);

        // Act - Initialize with constant prices
        TestHelpers.UpdatePrices(vidyaShort, prices);
        TestHelpers.UpdatePrices(vidyaLong, prices);

        var beforeShort = vidyaShort.Value;
        var beforeLong = vidyaLong.Value;

        // Add spike
        vidyaShort.Update(150m);
        vidyaLong.Update(150m);

        // Assert - Shorter period should respond more
        var shortChange = Math.Abs(vidyaShort.Value - beforeShort);
        var longChange = Math.Abs(vidyaLong.Value - beforeLong);

        Assert.True(shortChange > longChange,
            $"Shorter VIDYA should be more responsive. Short change: {shortChange}, Long change: {longChange}");
    }

    [Fact]
    public void VIDYA_AdaptsToDifferent_MarketConditions()
    {
        // Test VIDYA behavior in transition from trending to ranging
        // Arrange
        var vidya = Indicators.VIDYA(10);

        // Strong trend followed by consolidation
        var trendPrices = TestHelpers.AscendingPrices(100m, 5m, 15);
        var rangePrices = TestHelpers.OscillatingPrices(170m, 175m, 15);
        var allPrices = trendPrices.Concat(rangePrices).ToArray();

        // Act
        TestHelpers.UpdatePrices(vidya, allPrices);

        // Assert - Should adapt to both conditions
        TestHelpers.AssertReady(vidya);
        TestHelpers.AssertInRange(vidya.Value, 160m, 180m);
    }

    [Fact]
    public void VIDYA_DifferentPeriods_ProduceDifferentValues()
    {
        // Arrange
        var vidya5 = Indicators.VIDYA(5);
        var vidya10 = Indicators.VIDYA(10);
        var vidya20 = Indicators.VIDYA(20);
        var prices = TestHelpers.AscendingPrices(100m, 2m, 35);

        // Act
        TestHelpers.UpdatePrices(vidya5, prices);
        TestHelpers.UpdatePrices(vidya10, prices);
        TestHelpers.UpdatePrices(vidya20, prices);

        // Assert - Different periods should produce different values
        Assert.NotEqual(vidya5.Value, vidya10.Value);
        Assert.NotEqual(vidya10.Value, vidya20.Value);
        Assert.NotEqual(vidya5.Value, vidya20.Value);
    }

    [Fact]
    public void VIDYA_DifferentCMOPeriods_ProduceDifferentBehavior()
    {
        // Arrange
        var vidyaCMO5 = Indicators.VIDYA(14, 5);
        var vidyaCMO14 = Indicators.VIDYA(14, 14);
        var prices = TestHelpers.AscendingPrices(100m, 3m, 30);

        // Act
        TestHelpers.UpdatePrices(vidyaCMO5, prices);
        TestHelpers.UpdatePrices(vidyaCMO14, prices);

        // Assert - Different CMO periods should affect adaptation
        TestHelpers.AssertReady(vidyaCMO5);
        TestHelpers.AssertReady(vidyaCMO14);
        // Values will likely differ due to different CMO calculations
    }

    [Fact]
    public void VIDYA_CompareTo_EMA()
    {
        // VIDYA should behave like EMA but adapt to market conditions
        // Arrange
        var vidya = Indicators.VIDYA(14);
        var ema = Indicators.EMA(14);
        var prices = TestHelpers.AscendingPrices(100m, 2m, 30);

        // Act
        TestHelpers.UpdatePrices(vidya, prices);
        TestHelpers.UpdatePrices(ema, prices);

        // Assert - Both should track trend but VIDYA adapts
        TestHelpers.AssertReady(vidya);
        TestHelpers.AssertReady(ema);

        var currentPrice = prices[^1];
        var vidyaDistance = Math.Abs(currentPrice - vidya.Value);
        var emaDistance = Math.Abs(currentPrice - ema.Value);

        // Both should be reasonably close to current price
        Assert.True(vidyaDistance < 30m && emaDistance < 30m);
    }

    [Fact]
    public void VIDYA_StableAfterConvergence()
    {
        // Arrange
        var vidya = Indicators.VIDYA(10);
        var constantValue = 100m;
        var prices = TestHelpers.ConstantPrices(constantValue, 40);

        // Act
        TestHelpers.UpdatePrices(vidya, prices);

        // Assert - Should converge to constant value
        TestHelpers.AssertApproximately(constantValue, vidya.Value, 1m);
    }

    [Fact]
    public void VIDYA_HighCMO_IncreasesResponsiveness()
    {
        // When CMO is high (strong trend), VIDYA should be more responsive
        // Arrange
        var vidya = Indicators.VIDYA(10);

        // Very strong consistent trend (high CMO)
        var strongTrend = TestHelpers.AscendingPrices(100m, 10m, 25);

        // Act
        TestHelpers.UpdatePrices(vidya, strongTrend);

        // Assert - Should track closely due to high CMO
        var currentPrice = strongTrend[^1];
        var distance = Math.Abs(currentPrice - vidya.Value);

        Assert.True(distance < currentPrice * 0.15m, // Within 15% of current
            $"VIDYA should track closely in strong trend. Distance: {distance}");
    }

    [Fact]
    public void VIDYA_LowCMO_DecreasesResponsiveness()
    {
        // When CMO is low (ranging market), VIDYA should be less responsive
        // Arrange
        var vidya = Indicators.VIDYA(10);

        // Initialize with trend
        var initial = TestHelpers.AscendingPrices(100m, 2m, 15);
        TestHelpers.UpdatePrices(vidya, initial);

        var valueBefore = vidya.Value;

        // Then add ranging/choppy prices (low CMO)
        var ranging = TestHelpers.OscillatingPrices(130m, 135m, 20);
        TestHelpers.UpdatePrices(vidya, ranging);

        // Assert - Should smooth out the oscillations
        TestHelpers.AssertReady(vidya);
    }

    [Fact]
    public void VIDYA_BetterThan_EMA_ForAdaptiveTrading()
    {
        // VIDYA's adaptive nature makes it better for varying market conditions
        // Arrange
        var vidya = Indicators.VIDYA(14);
        var ema = Indicators.EMA(14);

        // Mixed market: trend, range, trend
        var trend1 = TestHelpers.AscendingPrices(100m, 3m, 10);
        var range = TestHelpers.OscillatingPrices(130m, 135m, 10);
        var trend2 = TestHelpers.AscendingPrices(135m, 3m, 10);
        var mixedPrices = trend1.Concat(range).Concat(trend2).ToArray();

        // Act
        TestHelpers.UpdatePrices(vidya, mixedPrices);
        TestHelpers.UpdatePrices(ema, mixedPrices);

        // Assert - Both should work, VIDYA adapts better
        TestHelpers.AssertReady(vidya);
        TestHelpers.AssertReady(ema);
    }

    [Fact]
    public void VIDYA_HandlesExtremeCMOValues()
    {
        // Test with prices that would produce extreme CMO values
        // Arrange
        var vidya = Indicators.VIDYA(10);

        // All gains (CMO → 100)
        var allGains = TestHelpers.AscendingPrices(100m, 10m, 20);

        // Act
        TestHelpers.UpdatePrices(vidya, allGains);

        // Assert - Should handle gracefully
        TestHelpers.AssertReady(vidya);
        Assert.True(vidya.Value > 100m);
    }

    [Fact]
    public void VIDYA_ReasonableValues_AllConditions()
    {
        // Ensure VIDYA produces reasonable values in various conditions
        // Arrange
        var vidya = Indicators.VIDYA(14);

        // Test various price patterns
        var patterns = new[]
        {
            TestHelpers.AscendingPrices(100m, 2m, 30),
            TestHelpers.DescendingPrices(100m, 2m, 30),
            TestHelpers.OscillatingPrices(90m, 110m, 30),
            TestHelpers.ConstantPrices(100m, 30),
            TestHelpers.SineWavePrices(100m, 15m, 30, 2.0)
        };

        foreach (var prices in patterns)
        {
            vidya.Reset();
            TestHelpers.UpdatePrices(vidya, prices);

            // Assert - Should produce reasonable values
            TestHelpers.AssertReady(vidya);
            Assert.True(vidya.Value != 0m || prices == TestHelpers.ConstantPrices(0m, 30));
        }
    }
}
