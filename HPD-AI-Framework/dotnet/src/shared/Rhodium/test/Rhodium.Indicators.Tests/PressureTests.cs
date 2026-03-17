using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Buy/Sell Pressure indicator.
/// </summary>
public class PressureTests
{
    [Fact]
    public void Pressure_IsReadyAfterPeriod()
    {
        // Arrange
        var pressure = Indicators.Pressure(14);

        // Act & Assert
        for (int i = 0; i < 13; i++)
        {
            pressure.Update(TestHelpers.CreateBar(100m + i, 1000m));
            TestHelpers.AssertNotReady(pressure);
        }

        pressure.Update(TestHelpers.CreateBar(114m, 1000m));
        TestHelpers.AssertReady(pressure);
    }

    [Fact]
    public void Pressure_BoundedBetweenMinusHundredAndHundred()
    {
        // Arrange
        var pressure = Indicators.Pressure(14);

        // Act - Strong buying (close near high)
        for (int i = 0; i < 20; i++)
        {
            var low = 100m + i;
            var high = low + 10m;
            var close = high - 0.1m; // Very close to high
            pressure.Update(TestHelpers.CreateBar(low, high, low, close, 1000m));
        }

        // Assert
        TestHelpers.AssertInRange(pressure.Value, -100m, 100m);
        Assert.True(pressure.Value > 50m, $"Expected high positive pressure, got {pressure.Value}");

        // Reset and test selling pressure
        pressure.Reset();
        for (int i = 0; i < 20; i++)
        {
            var low = 100m - i;
            var high = low + 10m;
            var close = low + 0.1m; // Very close to low
            pressure.Update(TestHelpers.CreateBar(low, high, low, close, 1000m));
        }

        // Assert
        TestHelpers.AssertInRange(pressure.Value, -100m, 100m);
        Assert.True(pressure.Value < -50m, $"Expected high negative pressure, got {pressure.Value}");
    }

    [Fact]
    public void Pressure_HighOnCloseNearHigh()
    {
        // Arrange
        var pressure = Indicators.Pressure(10);

        // Act - Close consistently at high (100% buy pressure)
        for (int i = 0; i < 15; i++)
        {
            // High=110, Low=100, Close=110
            // Buy = (110-100) * 1000 = 10000
            // Sell = (110-110) * 1000 = 0
            // Pressure = (10000-0)/(10000+0) * 100 = 100
            pressure.Update(TestHelpers.CreateBar(100m, 110m, 100m, 110m, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(pressure);
        TestHelpers.AssertApproximately(100m, pressure.Value, 0.01m);
    }

    [Fact]
    public void Pressure_LowOnCloseNearLow()
    {
        // Arrange
        var pressure = Indicators.Pressure(10);

        // Act - Close consistently at low (100% sell pressure)
        for (int i = 0; i < 15; i++)
        {
            // High=110, Low=100, Close=100
            // Buy = (100-100) * 1000 = 0
            // Sell = (110-100) * 1000 = 10000
            // Pressure = (0-10000)/(0+10000) * 100 = -100
            pressure.Update(TestHelpers.CreateBar(100m, 110m, 100m, 100m, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(pressure);
        TestHelpers.AssertApproximately(-100m, pressure.Value, 0.01m);
    }

    [Fact]
    public void Pressure_NeutralAtMidpoint()
    {
        // Arrange
        var pressure = Indicators.Pressure(10);

        // Act - Close at exact midpoint
        for (int i = 0; i < 15; i++)
        {
            // High=110, Low=100, Close=105
            // Buy = (105-100) * 1000 = 5000
            // Sell = (110-105) * 1000 = 5000
            // Pressure = (5000-5000)/(5000+5000) * 100 = 0
            pressure.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(pressure);
        TestHelpers.AssertApproximately(0m, pressure.Value, 0.01m);
    }

    [Fact]
    public void Pressure_ResetsCorrectly()
    {
        // Arrange
        var pressure = Indicators.Pressure(14);
        for (int i = 0; i < 20; i++)
        {
            pressure.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Act
        pressure.Reset();

        // Assert
        TestHelpers.AssertNotReady(pressure);
        TestHelpers.AssertCount(0, pressure);
        Assert.Equal(0m, pressure.Value);
    }

    [Fact]
    public void Pressure_RespondsToVolumeChanges()
    {
        // Arrange
        var pressure1 = Indicators.Pressure(5);
        var pressure2 = Indicators.Pressure(5);

        // Act - Same close positions, different volumes
        for (int i = 0; i < 10; i++)
        {
            // Close near high
            pressure1.Update(TestHelpers.CreateBar(100m, 110m, 100m, 109m, 1000m));
            pressure2.Update(TestHelpers.CreateBar(100m, 110m, 100m, 109m, 10000m));
        }

        // Assert - Both should show strong buying
        Assert.True(pressure1.Value > 50m && pressure2.Value > 50m);
        TestHelpers.AssertReady(pressure1);
        TestHelpers.AssertReady(pressure2);
    }

    [Fact]
    public void Pressure_CountIncrementsCorrectly()
    {
        // Arrange
        var pressure = Indicators.Pressure(10);

        // Act & Assert
        Assert.Equal(0, pressure.Count);

        pressure.Update(TestHelpers.CreateBar(100m));
        Assert.Equal(1, pressure.Count);

        pressure.Update(TestHelpers.CreateBar(105m));
        Assert.Equal(2, pressure.Count);
    }

    [Fact]
    public void Pressure_ThrowsOnInvalidPeriod()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Indicators.Pressure(0));
        Assert.Throws<ArgumentException>(() => Indicators.Pressure(-1));
    }

    [Fact]
    public void Pressure_RollingWindow()
    {
        // Arrange
        var pressure = Indicators.Pressure(5);

        // Act - Start with neutral
        for (int i = 0; i < 10; i++)
        {
            pressure.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));
        }
        var neutralValue = pressure.Value;

        // Shift to buying pressure
        for (int i = 0; i < 5; i++)
        {
            pressure.Update(TestHelpers.CreateBar(100m, 110m, 100m, 109m, 1000m));
        }
        var buyingValue = pressure.Value;

        // Assert
        TestHelpers.AssertApproximately(0m, neutralValue, 0.1m);
        Assert.True(buyingValue > neutralValue,
            $"Pressure should increase with buying: {neutralValue} -> {buyingValue}");
    }

    [Fact]
    public void Pressure_WithBullishBars()
    {
        // Arrange
        var pressure = Indicators.Pressure(10);

        // Act
        for (int i = 0; i < 15; i++)
        {
            pressure.Update(TestHelpers.CreateBullishBar(100m + i * 2, 110m + i * 2, volume: 1000m));
        }

        // Assert - Bullish bars (close > open) should show buying pressure
        TestHelpers.AssertReady(pressure);
        Assert.True(pressure.Value > 0m, $"Expected positive pressure on bullish bars, got {pressure.Value}");
    }

    [Fact]
    public void Pressure_WithBearishBars()
    {
        // Arrange
        var pressure = Indicators.Pressure(10);

        // Act
        for (int i = 0; i < 15; i++)
        {
            pressure.Update(TestHelpers.CreateBearishBar(200m - i * 2, 190m - i * 2, volume: 1000m));
        }

        // Assert - Bearish bars (close < open) should show selling pressure
        TestHelpers.AssertReady(pressure);
        Assert.True(pressure.Value < 0m, $"Expected negative pressure on bearish bars, got {pressure.Value}");
    }

    [Fact]
    public void Pressure_WithZeroRange()
    {
        // Arrange
        var pressure = Indicators.Pressure(5);

        // Act - High = Low (no range)
        for (int i = 0; i < 10; i++)
        {
            pressure.Update(TestHelpers.CreateBar(100m, 100m, 100m, 100m, 1000m));
        }

        // Assert - Should be 0 (no pressure either way)
        TestHelpers.AssertReady(pressure);
        Assert.Equal(0m, pressure.Value);
    }

    [Fact]
    public void Pressure_ManualCalculation()
    {
        // Arrange
        var pressure = Indicators.Pressure(3);

        // Manual calculation:
        // Bar 1: H=110, L=100, C=108, V=1000
        // Buy1 = (108-100) * 1000 = 8000
        // Sell1 = (110-108) * 1000 = 2000

        // Bar 2: H=115, L=105, C=112, V=1500
        // Buy2 = (112-105) * 1500 = 10500
        // Sell2 = (115-112) * 1500 = 4500

        // Bar 3: H=120, L=110, C=118, V=2000
        // Buy3 = (118-110) * 2000 = 16000
        // Sell3 = (120-118) * 2000 = 4000

        // Total Buy = 8000 + 10500 + 16000 = 34500
        // Total Sell = 2000 + 4500 + 4000 = 10500
        // Pressure = (34500 - 10500) / (34500 + 10500) * 100 = 24000 / 45000 * 100 = 53.33

        var bar1 = TestHelpers.CreateBar(100m, 110m, 100m, 108m, 1000m);
        var bar2 = TestHelpers.CreateBar(105m, 115m, 105m, 112m, 1500m);
        var bar3 = TestHelpers.CreateBar(110m, 120m, 110m, 118m, 2000m);

        // Act
        pressure.Update(bar1);
        pressure.Update(bar2);
        pressure.Update(bar3);

        // Assert
        TestHelpers.AssertReady(pressure);
        TestHelpers.AssertApproximately(53.33m, pressure.Value, 0.1m);
    }

    [Fact]
    public void Pressure_ShortPeriod()
    {
        // Arrange
        var pressure = Indicators.Pressure(3);

        // Act
        pressure.Update(TestHelpers.CreateBar(100m, 110m, 100m, 109m, 1000m));
        pressure.Update(TestHelpers.CreateBar(110m, 120m, 110m, 119m, 1000m));
        pressure.Update(TestHelpers.CreateBar(120m, 130m, 120m, 129m, 1000m));

        // Assert
        TestHelpers.AssertReady(pressure);
        Assert.True(pressure.Value > 70m, "Close near high should produce strong buying pressure");
    }

    [Fact]
    public void Pressure_WithZeroVolume()
    {
        // Arrange
        var pressure = Indicators.Pressure(5);

        // Act
        for (int i = 0; i < 10; i++)
        {
            var volume = i % 2 == 0 ? 0m : 1000m;
            pressure.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, volume));
        }

        // Assert - Should handle gracefully
        TestHelpers.AssertReady(pressure);
    }

    [Fact]
    public void Pressure_OscillatingPattern()
    {
        // Arrange
        var pressure = Indicators.Pressure(10);

        // Act - Alternate between buying and selling pressure
        for (int i = 0; i < 20; i++)
        {
            if (i % 2 == 0)
            {
                pressure.Update(TestHelpers.CreateBar(100m, 110m, 100m, 109m, 1000m)); // Buy
            }
            else
            {
                pressure.Update(TestHelpers.CreateBar(100m, 110m, 100m, 101m, 1000m)); // Sell
            }
        }

        // Assert - Should be near neutral
        TestHelpers.AssertReady(pressure);
        TestHelpers.AssertInRange(pressure.Value, -30m, 30m);
    }

    [Fact]
    public void Pressure_AsymmetricClose()
    {
        // Arrange
        var pressure = Indicators.Pressure(5);

        // Act - Close closer to high (60% up from low)
        for (int i = 0; i < 10; i++)
        {
            // Range = 100-110 (10 points)
            // Close at 106 (6 points from low, 4 points from high)
            // Buy = 6 * 1000 = 6000
            // Sell = 4 * 1000 = 4000
            // Pressure = (6000-4000)/(6000+4000) * 100 = 2000/10000 * 100 = 20
            pressure.Update(TestHelpers.CreateBar(100m, 110m, 100m, 106m, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(pressure);
        TestHelpers.AssertApproximately(20m, pressure.Value, 0.1m);
    }

    [Fact]
    public void Pressure_VolumeWeighting()
    {
        // Arrange
        var pressure = Indicators.Pressure(3);

        // Act - High volume bar with strong buying should dominate
        pressure.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));  // Neutral, normal volume
        pressure.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));  // Neutral, normal volume
        pressure.Update(TestHelpers.CreateBar(100m, 110m, 100m, 109m, 10000m)); // Strong buy, high volume

        // Assert - High volume buying should pull indicator positive
        TestHelpers.AssertReady(pressure);
        Assert.True(pressure.Value > 20m, $"High volume buying should dominate, got {pressure.Value}");
    }
}
