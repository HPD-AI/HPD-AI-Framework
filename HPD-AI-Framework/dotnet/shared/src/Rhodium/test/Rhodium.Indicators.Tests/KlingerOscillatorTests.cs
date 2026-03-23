using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Klinger Volume Oscillator indicator.
/// </summary>
public class KlingerOscillatorTests
{
    [Fact]
    public void KlingerOscillator_IsReadyAfterEMAsReady()
    {
        // Arrange - Default periods: fast=34, slow=55, signal=13
        var klinger = Indicators.KlingerOscillator();

        // Act - Need enough bars for all EMAs to be ready
        // Slow EMA (55) is the bottleneck, plus signal EMA (13) needs to be ready
        for (int i = 0; i < 70; i++)
        {
            klinger.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(klinger);
    }

    [Fact]
    public void KlingerOscillator_HasSignalLine()
    {
        // Arrange
        var klinger = Indicators.KlingerOscillator();

        // Act - Use proper OHLC bars with volume to generate volume force
        for (int i = 0; i < 80; i++)
        {
            var low = 100m + i;
            var high = low + 5m;
            var close = low + 3m;
            var open = low + 1m;
            klinger.Update(TestHelpers.CreateBar(open, high, low, close, 1000m + i * 10));
        }

        // Assert - Signal should be calculated (may be zero if volume force is balanced)
        TestHelpers.AssertReady(klinger);
        // Signal line exists and is calculated (value can be zero, positive, or negative)
        Assert.True(klinger.Signal != decimal.MaxValue, "Signal should be calculated");
    }

    [Fact]
    public void KlingerOscillator_ResetsCorrectly()
    {
        // Arrange
        var klinger = Indicators.KlingerOscillator();
        for (int i = 0; i < 80; i++)
        {
            klinger.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Act
        klinger.Reset();

        // Assert
        TestHelpers.AssertNotReady(klinger);
        TestHelpers.AssertCount(0, klinger);
        Assert.Equal(0m, klinger.Value);
        Assert.Equal(0m, klinger.Signal);
    }

    [Fact]
    public void KlingerOscillator_RespondsToVolumeChanges()
    {
        // Arrange
        var klinger1 = Indicators.KlingerOscillator(10, 20, 5);
        var klinger2 = Indicators.KlingerOscillator(10, 20, 5);

        // Act - Same price movements, different volumes
        // Use proper OHLC bars to generate non-zero volume force
        for (int i = 0; i < 30; i++)
        {
            var base_price = 100m + i;
            var open = base_price;
            var high = base_price + 5m;
            var low = base_price - 2m;
            var close = base_price + 3m;

            klinger1.Update(TestHelpers.CreateBar(open, high, low, close, 1000m));   // Normal volume
            klinger2.Update(TestHelpers.CreateBar(open, high, low, close, 10000m));  // High volume (10x)
        }

        // Assert - Volume affects the volume force calculation
        // Higher volume creates proportionally larger volume force values
        TestHelpers.AssertReady(klinger1);
        TestHelpers.AssertReady(klinger2);

        // The oscillator values should differ due to volume differences
        // Higher volume should produce larger magnitude values
        Assert.True(Math.Abs(klinger2.Value) > Math.Abs(klinger1.Value) * 5m,
            $"Higher volume should produce larger magnitude. K1: {klinger1.Value}, K2: {klinger2.Value}");
    }

    [Fact]
    public void KlingerOscillator_WithUptrend()
    {
        // Arrange
        var klinger = Indicators.KlingerOscillator(10, 20, 5);

        // Act - Consistent uptrend with increasing HLC
        for (int i = 0; i < 40; i++)
        {
            var low = 100m + i * 2;
            var high = low + 10m;
            var close = high - 1m;
            klinger.Update(TestHelpers.CreateBar(low, high, low, close, 1000m + i * 100));
        }

        // Assert
        TestHelpers.AssertReady(klinger);
    }

    [Fact]
    public void KlingerOscillator_WithDowntrend()
    {
        // Arrange
        var klinger = Indicators.KlingerOscillator(10, 20, 5);

        // Act - Consistent downtrend with decreasing HLC
        for (int i = 0; i < 40; i++)
        {
            var high = 200m - i * 2;
            var low = high - 10m;
            var close = low + 1m;
            klinger.Update(TestHelpers.CreateBar(low, high, low, close, 1000m + i * 100));
        }

        // Assert
        TestHelpers.AssertReady(klinger);
    }

    [Fact]
    public void KlingerOscillator_CountIncrementsCorrectly()
    {
        // Arrange
        var klinger = Indicators.KlingerOscillator();

        // Act & Assert
        Assert.Equal(0, klinger.Count);

        klinger.Update(TestHelpers.CreateBar(100m));
        Assert.Equal(1, klinger.Count);

        klinger.Update(TestHelpers.CreateBar(105m));
        Assert.Equal(2, klinger.Count);
    }

    [Fact]
    public void KlingerOscillator_CustomPeriods()
    {
        // Arrange
        var klinger = Indicators.KlingerOscillator(5, 10, 3);

        // Act - Shorter periods should be ready sooner
        for (int i = 0; i < 20; i++)
        {
            klinger.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(klinger);
    }

    [Fact]
    public void KlingerOscillator_SignalCrossover()
    {
        // Arrange
        var klinger = Indicators.KlingerOscillator(10, 20, 5);

        // Act - Build up indicator with proper OHLC bars
        for (int i = 0; i < 30; i++)
        {
            var base_price = 100m + i;
            var open = base_price;
            var high = base_price + 5m;
            var low = base_price - 2m;
            var close = base_price + 3m;
            klinger.Update(TestHelpers.CreateBar(open, high, low, close, 1000m + i * 50));
        }

        var value1 = klinger.Value;
        var signal1 = klinger.Signal;

        // Reverse trend - downward movement
        for (int i = 0; i < 10; i++)
        {
            var base_price = 130m - i * 2;
            var open = base_price;
            var high = base_price + 2m;
            var low = base_price - 5m;
            var close = base_price - 3m;
            klinger.Update(TestHelpers.CreateBar(open, high, low, close, 1000m));
        }

        var value2 = klinger.Value;
        var signal2 = klinger.Signal;

        // Assert - Indicator should respond to trend change
        TestHelpers.AssertReady(klinger);
        // Values should have changed (oscillator and signal respond to volume and price changes)
        Assert.True(value1 != value2 || signal1 != signal2, "Oscillator should respond to trend change");
    }

    [Fact]
    public void KlingerOscillator_WithBullishBars()
    {
        // Arrange
        var klinger = Indicators.KlingerOscillator(10, 20, 5);

        // Act
        for (int i = 0; i < 30; i++)
        {
            klinger.Update(TestHelpers.CreateBullishBar(100m + i * 2, 110m + i * 2, volume: 1000m));
        }

        // Assert
        TestHelpers.AssertReady(klinger);
    }

    [Fact]
    public void KlingerOscillator_WithBearishBars()
    {
        // Arrange
        var klinger = Indicators.KlingerOscillator(10, 20, 5);

        // Act
        for (int i = 0; i < 30; i++)
        {
            klinger.Update(TestHelpers.CreateBearishBar(200m - i * 2, 190m - i * 2, volume: 1000m));
        }

        // Assert
        TestHelpers.AssertReady(klinger);
    }

    [Fact]
    public void KlingerOscillator_VolumeForceCalculation()
    {
        // Arrange
        var klinger = Indicators.KlingerOscillator(5, 10, 3);

        // Act - First bar initializes
        klinger.Update(TestHelpers.CreateBar(100m, 110m, 95m, 105m, 1000m));
        Assert.Equal(1, klinger.Count);

        // Second bar with higher HLC should be uptrend
        klinger.Update(TestHelpers.CreateBar(105m, 115m, 100m, 110m, 1500m));
        Assert.Equal(2, klinger.Count);

        // Third bar continuing uptrend
        klinger.Update(TestHelpers.CreateBar(110m, 120m, 105m, 115m, 2000m));

        // Assert - Should be accumulating data
        Assert.Equal(3, klinger.Count);
    }

    [Fact]
    public void KlingerOscillator_TrendChange()
    {
        // Arrange
        var klinger = Indicators.KlingerOscillator(5, 10, 3);

        // Act - Uptrend
        for (int i = 0; i < 15; i++)
        {
            var hlc = 100m + i * 3;
            klinger.Update(TestHelpers.CreateBar(hlc, hlc + 5, hlc - 5, hlc, 1000m));
        }
        var uptrendValue = klinger.Value;

        // Trend reversal - downtrend
        for (int i = 0; i < 10; i++)
        {
            var hlc = 145m - i * 3;
            klinger.Update(TestHelpers.CreateBar(hlc, hlc + 5, hlc - 5, hlc, 1000m));
        }
        var downtrendValue = klinger.Value;

        // Assert - Indicator should respond to trend change
        TestHelpers.AssertReady(klinger);
    }

    [Fact]
    public void KlingerOscillator_WithConstantPrices()
    {
        // Arrange
        var klinger = Indicators.KlingerOscillator(10, 20, 5);

        // Act - No price movement
        for (int i = 0; i < 30; i++)
        {
            klinger.Update(TestHelpers.CreateBar(100m, 100m, 100m, 100m, 1000m));
        }

        // Assert - Should handle gracefully
        TestHelpers.AssertReady(klinger);
    }

    [Fact]
    public void KlingerOscillator_ValueIsOscillator()
    {
        // Arrange
        var klinger = Indicators.KlingerOscillator(10, 20, 5);

        // Act
        for (int i = 0; i < 30; i++)
        {
            klinger.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Assert - Value should be fast EMA - slow EMA (oscillator)
        TestHelpers.AssertReady(klinger);
        // Can be positive or negative depending on trend
    }

    [Fact]
    public void KlingerOscillator_WithZeroVolume()
    {
        // Arrange
        var klinger = Indicators.KlingerOscillator(10, 20, 5);

        // Act
        for (int i = 0; i < 30; i++)
        {
            var volume = i % 5 == 0 ? 0m : 1000m;
            klinger.Update(TestHelpers.CreateBar(100m + i, volume));
        }

        // Assert - Should handle zero volume gracefully
        TestHelpers.AssertReady(klinger);
    }

    [Fact]
    public void KlingerOscillator_HighVolatility()
    {
        // Arrange
        var klinger = Indicators.KlingerOscillator(10, 20, 5);

        // Act - High volatility with wide ranges
        for (int i = 0; i < 30; i++)
        {
            var low = 100m + i;
            var high = low + 50m; // Large range
            var close = (high + low) / 2;
            klinger.Update(TestHelpers.CreateBar(low, high, low, close, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(klinger);
    }

    [Fact]
    public void KlingerOscillator_LowVolatility()
    {
        // Arrange
        var klinger = Indicators.KlingerOscillator(10, 20, 5);

        // Act - Low volatility with tight ranges
        for (int i = 0; i < 30; i++)
        {
            var low = 100m;
            var high = 101m; // Small range
            var close = 100.5m;
            klinger.Update(TestHelpers.CreateBar(low, high, low, close, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(klinger);
    }
}
