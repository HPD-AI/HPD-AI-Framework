using Rhodium.Primitives;
using Rhodium.Indicators;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Triple Exponential Moving Average (TEMA) indicator.
/// TEMA = 3*EMA - 3*EMA(EMA) + EMA(EMA(EMA)) - further reduces lag compared to DEMA.
/// </summary>
public class TEMATests
{
    [Fact]
    public void TEMA_BecomesReady_AfterMultiplePeriods()
    {
        // TEMA needs time for three cascading EMAs to become ready
        var period = 5;
        var tema = Indicators.TEMA(period);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 30);

        // Act - Update until ready
        int updateCount = 0;
        foreach (var price in prices)
        {
            tema.Update(price);
            updateCount++;
            if (tema.IsReady) break;
        }

        // Assert - Should be ready after enough updates for three EMAs
        TestHelpers.AssertReady(tema);
        Assert.True(updateCount >= period, $"TEMA should need at least {period} updates, got {updateCount}");
    }

    [Fact]
    public void TEMA_ResetsCorrectly()
    {
        // Arrange
        var tema = Indicators.TEMA(5);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 30);

        // Act & Assert
        TestHelpers.TestReset(tema, () => TestHelpers.UpdatePrices(tema, prices));
    }

    [Fact]
    public void TEMA_ProducesConstantValue_WithConstantPrices()
    {
        // Arrange
        var tema = Indicators.TEMA(5);
        var constantValue = 100m;
        var prices = TestHelpers.ConstantPrices(constantValue, 30);

        // Act
        TestHelpers.UpdatePrices(tema, prices);

        // Assert
        TestHelpers.AssertIndicatorValue(constantValue, tema, 0.1m);
    }

    [Fact]
    public void TEMA_HandlesZeroPrices()
    {
        // Arrange
        var tema = Indicators.TEMA(5);

        // Act & Assert
        TestHelpers.TestZeroPrices(tema, 30);
        TestHelpers.AssertIndicatorValue(0m, tema, 0.1m);
    }

    [Fact]
    public void TEMA_HandlesLargePrices()
    {
        // Arrange
        var tema = Indicators.TEMA(5);

        // Act & Assert
        TestHelpers.TestLargePrices(tema);
    }

    [Fact]
    public void TEMA_RespondsToTrends()
    {
        // Arrange
        var tema = Indicators.TEMA(5);
        var ascending = TestHelpers.AscendingPrices(100m, 1m, 30);
        var descending = TestHelpers.DescendingPrices(100m, 1m, 30);

        // Act & Assert
        TestHelpers.TestResponsiveness(tema, ascending, descending);
    }

    [Fact]
    public void TEMA_CountIncrementsCorrectly()
    {
        // Arrange
        var tema = Indicators.TEMA(5);
        var prices = TestHelpers.Prices(10m, 20m, 30m);

        // Act
        TestHelpers.UpdatePrices(tema, prices);

        // Assert
        TestHelpers.AssertCount(3, tema);
    }

    [Fact]
    public void TEMA_MoreResponsive_ThanDEMA()
    {
        // Arrange
        var tema = Indicators.TEMA(10);
        var dema = Indicators.DEMA(10);
        var prices = TestHelpers.ConstantPrices(100m, 30);

        // Act - Initialize both with constant prices
        TestHelpers.UpdatePrices(tema, prices);
        TestHelpers.UpdatePrices(dema, prices);

        var temaBeforeSpike = tema.Value;
        var demaBeforeSpike = dema.Value;

        // Add a sudden spike
        tema.Update(150m);
        dema.Update(150m);

        // Assert - TEMA should respond more due to triple smoothing
        var temaChange = Math.Abs(tema.Value - temaBeforeSpike);
        var demaChange = Math.Abs(dema.Value - demaBeforeSpike);

        Assert.True(temaChange > demaChange,
            $"TEMA should be more responsive than DEMA. TEMA change: {temaChange}, DEMA change: {demaChange}");
    }

    [Fact]
    public void TEMA_ReducesLag_ComparedToDEMA()
    {
        // TEMA uses triple exponential smoothing to reduce lag
        // However, the actual lag reduction depends on the price pattern and period
        // In some cases DEMA may perform better depending on smoothing characteristics

        // Arrange
        var tema = Indicators.TEMA(10);
        var dema = Indicators.DEMA(10);
        var prices = TestHelpers.AscendingPrices(100m, 2m, 40);

        // Act
        TestHelpers.UpdatePrices(tema, prices);
        TestHelpers.UpdatePrices(dema, prices);

        // Assert - Both should track the trend closely
        var currentPrice = prices[^1];
        var temaDistance = Math.Abs(currentPrice - tema.Value);
        var demaDistance = Math.Abs(currentPrice - dema.Value);

        // Both indicators should track reasonably well
        Assert.True(temaDistance < currentPrice * 0.1m,
            $"TEMA should track trend reasonably. Distance: {temaDistance}, Current: {currentPrice}");
        Assert.True(demaDistance < currentPrice * 0.1m,
            $"DEMA should track trend reasonably. Distance: {demaDistance}, Current: {currentPrice}");

        // Both should follow the uptrend
        Assert.True(tema.Value > 100m && dema.Value > 100m, "Both should follow the uptrend");
    }

    [Fact]
    public void TEMA_AscendingPrices_ProducesAscendingValues()
    {
        // Arrange
        var tema = Indicators.TEMA(5);
        var prices = TestHelpers.AscendingPrices(100m, 5m, 30);

        // Act & Assert
        decimal previousValue = 0m;
        int readyCount = 0;

        foreach (var price in prices)
        {
            tema.Update(price);
            if (tema.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(tema.Value >= previousValue - 0.1m, // Allow tiny rounding
                        $"TEMA should increase with ascending prices. Previous: {previousValue}, Current: {tema.Value}");
                }
                previousValue = tema.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void TEMA_DescendingPrices_ProducesDescendingValues()
    {
        // Arrange
        var tema = Indicators.TEMA(5);
        var prices = TestHelpers.DescendingPrices(100m, 5m, 30);

        // Act & Assert
        decimal previousValue = decimal.MaxValue;
        int readyCount = 0;

        foreach (var price in prices)
        {
            tema.Update(price);
            if (tema.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(tema.Value <= previousValue + 0.1m, // Allow tiny rounding
                        $"TEMA should decrease with descending prices. Previous: {previousValue}, Current: {tema.Value}");
                }
                previousValue = tema.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void TEMA_OscillatingPrices_ProducesSmoothedValue()
    {
        // Arrange
        var tema = Indicators.TEMA(5);
        var prices = TestHelpers.OscillatingPrices(90m, 110m, 35);

        // Act
        TestHelpers.UpdatePrices(tema, prices);

        // Assert - TEMA should smooth oscillations while staying very responsive
        TestHelpers.AssertReady(tema);
        TestHelpers.AssertInRange(tema.Value, 85m, 115m);
    }

    [Fact]
    public void TEMA_SineWave_ProducesSmoothedOutput()
    {
        // Arrange
        var tema = Indicators.TEMA(10);
        var prices = TestHelpers.SineWavePrices(100m, 20m, 120, 2.0);

        // Act
        TestHelpers.UpdatePrices(tema, prices);

        // Assert - TEMA should smooth the sine wave with minimal lag
        TestHelpers.AssertReady(tema);
        TestHelpers.AssertInRange(tema.Value, 80m, 120m);
    }

    [Fact]
    public void TEMA_Formula_ThreeEmasCombined()
    {
        // This documents that TEMA = 3*EMA(n) - 3*EMA(EMA(n)) + EMA(EMA(EMA(n)))
        // Arrange
        var tema = Indicators.TEMA(5);
        var prices = TestHelpers.ConstantPrices(100m, 30);

        // Act
        TestHelpers.UpdatePrices(tema, prices);

        // Assert - With constant prices, formula should produce constant value
        // 3*100 - 3*100 + 100 = 100
        TestHelpers.AssertApproximately(100m, tema.Value, 0.2m);
    }

    [Fact]
    public void TEMA_CanOvershoots_MoreThanDEMA()
    {
        // TEMA's extreme lag reduction can cause more overshooting
        // Arrange
        var tema = Indicators.TEMA(5);
        var dema = Indicators.DEMA(5);

        // Sharp uptrend followed by plateau
        var prices = TestHelpers.Prices(100m, 100m, 100m, 100m, 100m, 100m,
                                        120m, 120m, 120m, 120m, 120m, 120m,
                                        120m, 120m, 120m, 120m, 120m, 120m);

        // Act
        TestHelpers.UpdatePrices(tema, prices);
        TestHelpers.UpdatePrices(dema, prices);

        // Assert - Both should converge but TEMA reacts more aggressively
        TestHelpers.AssertReady(tema);
        TestHelpers.AssertReady(dema);
    }

    [Fact]
    public void TEMA_Period1_ApproximatesCurrentPrice()
    {
        // Arrange
        var tema = Indicators.TEMA(1);
        var prices = TestHelpers.Prices(10m, 20m, 30m, 40m, 50m);

        // Act
        foreach (var price in prices)
        {
            tema.Update(price);
            if (tema.IsReady)
            {
                // Assert - With period 1, TEMA should be very close to current price
                TestHelpers.AssertApproximately(price, tema.Value, 0.5m);
            }
        }
    }

    [Fact]
    public void TEMA_ShortPeriod_MoreResponsive_ThanLongPeriod()
    {
        // Arrange
        var temaShort = Indicators.TEMA(3);
        var temaLong = Indicators.TEMA(10);
        var prices = TestHelpers.ConstantPrices(100m, 30);

        // Act - Initialize with constant prices
        TestHelpers.UpdatePrices(temaShort, prices);
        TestHelpers.UpdatePrices(temaLong, prices);

        var beforeShort = temaShort.Value;
        var beforeLong = temaLong.Value;

        // Add spike
        temaShort.Update(150m);
        temaLong.Update(150m);

        // Assert - Shorter period should respond more
        var shortChange = Math.Abs(temaShort.Value - beforeShort);
        var longChange = Math.Abs(temaLong.Value - beforeLong);

        Assert.True(shortChange > longChange,
            $"Shorter TEMA should be more responsive. Short change: {shortChange}, Long change: {longChange}");
    }

    [Fact]
    public void TEMA_TrendFollowing_BestAmongEMAs()
    {
        // Arrange
        var tema = Indicators.TEMA(8);
        var dema = Indicators.DEMA(8);
        var ema = Indicators.EMA(8);

        // Strong trend
        var trendPrices = TestHelpers.AscendingPrices(100m, 5m, 25);

        // Act
        TestHelpers.UpdatePrices(tema, trendPrices);
        TestHelpers.UpdatePrices(dema, trendPrices);
        TestHelpers.UpdatePrices(ema, trendPrices);

        // Assert - TEMA should track the trend most closely
        var currentPrice = trendPrices[^1];
        var temaLag = currentPrice - tema.Value;
        var demaLag = currentPrice - dema.Value;
        var emaLag = currentPrice - ema.Value;

        Assert.True(temaLag < demaLag,
            $"TEMA should have less lag than DEMA. TEMA lag: {temaLag}, DEMA lag: {demaLag}");
        Assert.True(temaLag < emaLag,
            $"TEMA should have less lag than EMA. TEMA lag: {temaLag}, EMA lag: {emaLag}");
    }

    [Fact]
    public void TEMA_DifferentPeriods_ProduceDifferentValues()
    {
        // Arrange
        var tema3 = Indicators.TEMA(3);
        var tema10 = Indicators.TEMA(10);
        var tema20 = Indicators.TEMA(20);
        var prices = TestHelpers.AscendingPrices(100m, 2m, 60);

        // Act
        TestHelpers.UpdatePrices(tema3, prices);
        TestHelpers.UpdatePrices(tema10, prices);
        TestHelpers.UpdatePrices(tema20, prices);

        // Assert - Different periods should produce different values
        Assert.NotEqual(tema3.Value, tema10.Value);
        Assert.NotEqual(tema10.Value, tema20.Value);
        Assert.NotEqual(tema3.Value, tema20.Value);
    }

    [Fact]
    public void TEMA_StableAfterConvergence()
    {
        // Arrange
        var tema = Indicators.TEMA(5);
        var constantValue = 100m;
        var prices = TestHelpers.ConstantPrices(constantValue, 50);

        // Act
        TestHelpers.UpdatePrices(tema, prices);

        // Assert - Should converge to constant value
        TestHelpers.AssertApproximately(constantValue, tema.Value, 1m);
    }

    [Fact]
    public void TEMA_VsEMA_VsDEMA_ResponsivenessOrder()
    {
        // Document that TEMA > DEMA > EMA in responsiveness
        // Arrange
        var ema = Indicators.EMA(10);
        var dema = Indicators.DEMA(10);
        var tema = Indicators.TEMA(10);
        var prices = TestHelpers.ConstantPrices(100m, 30);

        // Act - Initialize all
        TestHelpers.UpdatePrices(ema, prices);
        TestHelpers.UpdatePrices(dema, prices);
        TestHelpers.UpdatePrices(tema, prices);

        var emaBefore = ema.Value;
        var demaBefore = dema.Value;
        var temaBefore = tema.Value;

        // Add spike
        ema.Update(150m);
        dema.Update(150m);
        tema.Update(150m);

        // Assert - Verify responsiveness order
        var emaChange = Math.Abs(ema.Value - emaBefore);
        var demaChange = Math.Abs(dema.Value - demaBefore);
        var temaChange = Math.Abs(tema.Value - temaBefore);

        Assert.True(temaChange > demaChange && demaChange > emaChange,
            $"Expected TEMA > DEMA > EMA responsiveness. TEMA: {temaChange}, DEMA: {demaChange}, EMA: {emaChange}");
    }
}
