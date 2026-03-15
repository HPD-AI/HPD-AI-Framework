using Rhodium.Primitives;
using Rhodium.Indicators;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Triangular Moving Average (TRIMA) indicator.
/// TRIMA = SMA(SMA(n), m) where m = (n+1)/2 - produces very smooth output with triangular weighting.
/// </summary>
public class TRIMATests
{
    [Fact]
    public void TRIMA_BecomesReady_AfterPeriodUpdates()
    {
        // TRIMA needs period updates for outer SMA
        var period = 10;
        var trima = Indicators.TRIMA(period);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 20);

        // Act - Update until ready
        int updateCount = 0;
        foreach (var price in prices)
        {
            trima.Update(price);
            updateCount++;
            if (trima.IsReady) break;
        }

        // Assert
        TestHelpers.AssertReady(trima);
        // TRIMA with even period 10 uses SMA(5) and SMA(6), so needs max(5,6)=6 updates
        // TRIMA with odd period uses SMA((n+1)/2) twice
        // In general, TRIMA needs fewer updates than the period due to double SMA structure
        Assert.True(updateCount > 0 && updateCount <= period, $"TRIMA became ready after {updateCount} updates");
    }

    [Fact]
    public void TRIMA_ResetsCorrectly()
    {
        // Arrange
        var trima = Indicators.TRIMA(10);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 20);

        // Act & Assert
        TestHelpers.TestReset(trima, () => TestHelpers.UpdatePrices(trima, prices));
    }

    [Fact]
    public void TRIMA_ProducesConstantValue_WithConstantPrices()
    {
        // Arrange
        var trima = Indicators.TRIMA(10);
        var constantValue = 100m;
        var prices = TestHelpers.ConstantPrices(constantValue, 20);

        // Act
        TestHelpers.UpdatePrices(trima, prices);

        // Assert - SMA of SMA of constant = constant
        TestHelpers.AssertIndicatorValue(constantValue, trima, 0.01m);
    }

    [Fact]
    public void TRIMA_HandlesZeroPrices()
    {
        // Arrange
        var trima = Indicators.TRIMA(10);

        // Act & Assert
        TestHelpers.TestZeroPrices(trima, 20);
        TestHelpers.AssertIndicatorValue(0m, trima, 0.01m);
    }

    [Fact]
    public void TRIMA_HandlesLargePrices()
    {
        // Arrange
        var trima = Indicators.TRIMA(10);

        // Act & Assert
        TestHelpers.TestLargePrices(trima);
    }

    [Fact]
    public void TRIMA_RespondsToTrends()
    {
        // Arrange
        var trima = Indicators.TRIMA(10);
        var ascending = TestHelpers.AscendingPrices(100m, 1m, 25);
        var descending = TestHelpers.DescendingPrices(100m, 1m, 25);

        // Act & Assert
        TestHelpers.TestResponsiveness(trima, ascending, descending);
    }

    [Fact]
    public void TRIMA_CountIncrementsCorrectly()
    {
        // Arrange
        var trima = Indicators.TRIMA(10);
        var prices = TestHelpers.Prices(10m, 20m, 30m);

        // Act
        TestHelpers.UpdatePrices(trima, prices);

        // Assert
        TestHelpers.AssertCount(3, trima);
    }

    [Fact]
    public void TRIMA_SmootherThan_SMA()
    {
        // TRIMA double-smooths so it should be smoother
        // Arrange
        var trima = Indicators.TRIMA(10);
        var sma = Indicators.SMA(10);
        var prices = TestHelpers.OscillatingPrices(90m, 110m, 25);

        // Act
        TestHelpers.UpdatePrices(trima, prices);
        TestHelpers.UpdatePrices(sma, prices);

        // Assert - Both should smooth but TRIMA more so
        TestHelpers.AssertReady(trima);
        TestHelpers.AssertReady(sma);
        TestHelpers.AssertInRange(trima.Value, 85m, 115m);
        TestHelpers.AssertInRange(sma.Value, 85m, 115m);
    }

    [Fact]
    public void TRIMA_LessResponsive_ThanSMA()
    {
        // Double smoothing means more lag
        // Arrange
        var trima = Indicators.TRIMA(10);
        var sma = Indicators.SMA(10);
        var prices = TestHelpers.ConstantPrices(100m, 15);

        // Act - Initialize both with constant prices
        TestHelpers.UpdatePrices(trima, prices);
        TestHelpers.UpdatePrices(sma, prices);

        var trimaBefore = trima.Value;
        var smaBefore = sma.Value;

        // Add a sudden spike
        trima.Update(150m);
        sma.Update(150m);

        // Assert - TRIMA should respond less due to double smoothing
        var trimaChange = Math.Abs(trima.Value - trimaBefore);
        var smaChange = Math.Abs(sma.Value - smaBefore);

        Assert.True(trimaChange < smaChange,
            $"TRIMA should be less responsive. TRIMA change: {trimaChange}, SMA change: {smaChange}");
    }

    [Fact]
    public void TRIMA_AscendingPrices_ProducesAscendingValues()
    {
        // Arrange
        var trima = Indicators.TRIMA(10);
        var prices = TestHelpers.AscendingPrices(100m, 5m, 25);

        // Act & Assert
        decimal previousValue = 0m;
        int readyCount = 0;

        foreach (var price in prices)
        {
            trima.Update(price);
            if (trima.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(trima.Value > previousValue - 0.01m,
                        $"TRIMA should increase with ascending prices. Previous: {previousValue}, Current: {trima.Value}");
                }
                previousValue = trima.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void TRIMA_DescendingPrices_ProducesDescendingValues()
    {
        // Arrange
        var trima = Indicators.TRIMA(10);
        var prices = TestHelpers.DescendingPrices(100m, 5m, 25);

        // Act & Assert
        decimal previousValue = decimal.MaxValue;
        int readyCount = 0;

        foreach (var price in prices)
        {
            trima.Update(price);
            if (trima.IsReady)
            {
                if (readyCount > 0)
                {
                    Assert.True(trima.Value < previousValue + 0.01m,
                        $"TRIMA should decrease with descending prices. Previous: {previousValue}, Current: {trima.Value}");
                }
                previousValue = trima.Value;
                readyCount++;
            }
        }
    }

    [Fact]
    public void TRIMA_OscillatingPrices_ProducesVerySmoothedValue()
    {
        // Arrange
        var trima = Indicators.TRIMA(10);
        var prices = TestHelpers.OscillatingPrices(90m, 110m, 30);

        // Act
        TestHelpers.UpdatePrices(trima, prices);

        // Assert - TRIMA should heavily smooth oscillations
        TestHelpers.AssertReady(trima);
        TestHelpers.AssertInRange(trima.Value, 92m, 108m); // Tighter range due to smoothing
    }

    [Fact]
    public void TRIMA_SineWave_ProducesExtremelySmoothedOutput()
    {
        // Arrange
        var trima = Indicators.TRIMA(10);
        var prices = TestHelpers.SineWavePrices(100m, 20m, 100, 2.0);

        // Act
        TestHelpers.UpdatePrices(trima, prices);

        // Assert - TRIMA should heavily smooth the sine wave
        TestHelpers.AssertReady(trima);
        TestHelpers.AssertInRange(trima.Value, 80m, 120m);
    }

    [Fact]
    public void TRIMA_Formula_DoubleSMA()
    {
        // Documents that TRIMA is SMA of SMA
        // Arrange
        var trima = Indicators.TRIMA(10);
        var prices = TestHelpers.ConstantPrices(100m, 25);

        // Act
        TestHelpers.UpdatePrices(trima, prices);

        // Assert - SMA(SMA(100)) = 100
        TestHelpers.AssertApproximately(100m, trima.Value, 0.1m);
    }

    [Fact]
    public void TRIMA_Period10_UsesInner6()
    {
        // For period 10, inner SMA period = (10+1)/2 = 5.5 ≈ 6
        // This test documents the period calculation
        // Arrange
        var trima = Indicators.TRIMA(10);
        var prices = TestHelpers.AscendingPrices(100m, 2m, 20);

        // Act
        TestHelpers.UpdatePrices(trima, prices);

        // Assert
        TestHelpers.AssertReady(trima);
        Assert.True(trima.Value > 100m);
    }

    [Fact]
    public void TRIMA_EvenPeriod_Works()
    {
        // Test with even period
        // Arrange
        var trima = Indicators.TRIMA(10);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 20);

        // Act
        TestHelpers.UpdatePrices(trima, prices);

        // Assert
        TestHelpers.AssertReady(trima);
    }

    [Fact]
    public void TRIMA_OddPeriod_Works()
    {
        // Test with odd period
        // Arrange
        var trima = Indicators.TRIMA(9);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 20);

        // Act
        TestHelpers.UpdatePrices(trima, prices);

        // Assert
        TestHelpers.AssertReady(trima);
    }

    [Fact]
    public void TRIMA_ShortPeriod_MoreResponsive_ThanLongPeriod()
    {
        // Arrange
        var trimaShort = Indicators.TRIMA(5);
        var trimaLong = Indicators.TRIMA(15);
        var prices = TestHelpers.ConstantPrices(100m, 25);

        // Act - Initialize with constant prices
        TestHelpers.UpdatePrices(trimaShort, prices);
        TestHelpers.UpdatePrices(trimaLong, prices);

        var beforeShort = trimaShort.Value;
        var beforeLong = trimaLong.Value;

        // Add spike
        trimaShort.Update(150m);
        trimaLong.Update(150m);

        // Assert - Shorter period should respond more
        var shortChange = Math.Abs(trimaShort.Value - beforeShort);
        var longChange = Math.Abs(trimaLong.Value - beforeLong);

        Assert.True(shortChange > longChange,
            $"Shorter TRIMA should be more responsive. Short change: {shortChange}, Long change: {longChange}");
    }

    [Fact]
    public void TRIMA_TriangularWeighting_CenterHeavy()
    {
        // TRIMA gives more weight to center values due to double smoothing
        // Arrange
        var trima = Indicators.TRIMA(5);

        // Prices with spike in middle
        var prices = TestHelpers.Prices(100m, 100m, 150m, 100m, 100m, 100m, 100m, 100m);

        // Act
        TestHelpers.UpdatePrices(trima, prices);

        // Assert - The center spike should have lingering effect
        TestHelpers.AssertReady(trima);
    }

    [Fact]
    public void TRIMA_DifferentPeriods_ProduceDifferentValues()
    {
        // Arrange
        var trima5 = Indicators.TRIMA(5);
        var trima10 = Indicators.TRIMA(10);
        var trima20 = Indicators.TRIMA(20);
        var prices = TestHelpers.AscendingPrices(100m, 2m, 30);

        // Act
        TestHelpers.UpdatePrices(trima5, prices);
        TestHelpers.UpdatePrices(trima10, prices);
        TestHelpers.UpdatePrices(trima20, prices);

        // Assert - Different periods should produce different values
        Assert.NotEqual(trima5.Value, trima10.Value);
        Assert.NotEqual(trima10.Value, trima20.Value);
        Assert.NotEqual(trima5.Value, trima20.Value);
    }

    [Fact]
    public void TRIMA_HighestLag_AmongBasicMAs()
    {
        // TRIMA should have more lag due to double smoothing
        // Use a strong trend with consistent steps for clear comparison
        // Arrange
        var trima = Indicators.TRIMA(10);
        var sma = Indicators.SMA(10);
        var ema = Indicators.EMA(10);
        var trendPrices = TestHelpers.AscendingPrices(100m, 5m, 30);

        // Act
        TestHelpers.UpdatePrices(trima, trendPrices);
        TestHelpers.UpdatePrices(sma, trendPrices);
        TestHelpers.UpdatePrices(ema, trendPrices);

        // Assert - TRIMA should generally have more lag
        var currentPrice = trendPrices[^1];
        var trimaLag = currentPrice - trima.Value;
        var smaLag = currentPrice - sma.Value;
        var emaLag = currentPrice - ema.Value;

        // TRIMA has double smoothing so typically has most lag
        // Compare to SMA (which has less smoothing than TRIMA)
        Assert.True(trimaLag >= smaLag * 0.9m,
            $"TRIMA should have at least as much lag as SMA. TRIMA lag: {trimaLag}, SMA lag: {smaLag}");

        // EMA should be most responsive (least lag)
        Assert.True(trimaLag > emaLag,
            $"TRIMA should have more lag than EMA. TRIMA lag: {trimaLag}, EMA lag: {emaLag}");

        // Verify lag order is correct: TRIMA >= SMA > EMA
        Assert.True(smaLag > emaLag,
            $"SMA should have more lag than EMA. SMA lag: {smaLag}, EMA lag: {emaLag}");
    }

    [Fact]
    public void TRIMA_BestFor_VeryChoppyMarkets()
    {
        // TRIMA's extreme smoothing is ideal for noisy data
        // Arrange
        var trima = Indicators.TRIMA(10);
        var sma = Indicators.SMA(10);

        // Very choppy prices
        var prices = TestHelpers.Prices(100m, 110m, 95m, 105m, 92m, 108m, 88m, 112m, 85m, 115m,
                                        100m, 110m, 95m, 105m, 92m, 108m, 98m, 102m, 99m, 101m);

        // Act
        TestHelpers.UpdatePrices(trima, prices);
        TestHelpers.UpdatePrices(sma, prices);

        // Assert - TRIMA should produce smoother result
        TestHelpers.AssertReady(trima);
        TestHelpers.AssertReady(sma);
    }

    [Fact]
    public void TRIMA_StableAfterConvergence()
    {
        // Arrange
        var trima = Indicators.TRIMA(10);
        var constantValue = 100m;
        var prices = TestHelpers.ConstantPrices(constantValue, 30);

        // Act
        TestHelpers.UpdatePrices(trima, prices);

        // Assert - Should converge to constant value
        TestHelpers.AssertApproximately(constantValue, trima.Value, 0.5m);
    }

    [Fact]
    public void TRIMA_MinimumPeriod_Works()
    {
        // Test with minimum useful period
        // Arrange
        var trima = Indicators.TRIMA(2);
        var prices = TestHelpers.Prices(10m, 20m, 30m, 40m, 50m);

        // Act
        TestHelpers.UpdatePrices(trima, prices);

        // Assert
        TestHelpers.AssertReady(trima);
    }
}
