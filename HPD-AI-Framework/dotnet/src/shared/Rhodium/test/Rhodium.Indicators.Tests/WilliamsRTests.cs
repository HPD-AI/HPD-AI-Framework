using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Williams %R indicator.
/// </summary>
public class WilliamsRTests
{
    [Fact]
    public void WilliamsR_IsReadyAfterPeriod()
    {
        // Arrange
        var williams = Indicators.WilliamsR(14);

        // Act & Assert
        for (int i = 0; i < 13; i++)
        {
            williams.Update(TestHelpers.CreateBar(100m + i, 1000m));
            TestHelpers.AssertNotReady(williams);
        }

        williams.Update(TestHelpers.CreateBar(114m, 1000m));
        TestHelpers.AssertReady(williams);
    }

    [Fact]
    public void WilliamsR_BoundedBetweenMinusHundredAndZero()
    {
        // Arrange
        var williams = Indicators.WilliamsR(14);

        // Act - Various price movements
        for (int i = 0; i < 20; i++)
        {
            var low = 100m + i;
            var high = low + 10m;
            var close = low + (i % 10);
            williams.Update(TestHelpers.CreateBar(low, high, low, close, 1000m));
        }

        // Assert - Williams %R is always between -100 and 0
        TestHelpers.AssertReady(williams);
        TestHelpers.AssertInRange(williams.Value, -100m, 0m);
    }

    [Fact]
    public void WilliamsR_CloseAtHighGivesZero()
    {
        // Arrange
        var williams = Indicators.WilliamsR(5);

        // Act - Close at highest high
        williams.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));
        williams.Update(TestHelpers.CreateBar(105m, 115m, 105m, 110m, 1000m));
        williams.Update(TestHelpers.CreateBar(110m, 120m, 110m, 115m, 1000m));
        williams.Update(TestHelpers.CreateBar(115m, 125m, 115m, 120m, 1000m));
        williams.Update(TestHelpers.CreateBar(120m, 130m, 120m, 130m, 1000m)); // Close at highest

        // Assert - Close at highest high should give 0
        TestHelpers.AssertReady(williams);
        TestHelpers.AssertApproximately(0m, williams.Value, 0.01m);
    }

    [Fact]
    public void WilliamsR_CloseAtLowGivesMinusHundred()
    {
        // Arrange
        var williams = Indicators.WilliamsR(5);

        // Act - Close at lowest low
        williams.Update(TestHelpers.CreateBar(120m, 130m, 120m, 125m, 1000m));
        williams.Update(TestHelpers.CreateBar(115m, 125m, 115m, 120m, 1000m));
        williams.Update(TestHelpers.CreateBar(110m, 120m, 110m, 115m, 1000m));
        williams.Update(TestHelpers.CreateBar(105m, 115m, 105m, 110m, 1000m));
        williams.Update(TestHelpers.CreateBar(100m, 110m, 100m, 100m, 1000m)); // Close at lowest

        // Assert - Close at lowest low should give -100
        TestHelpers.AssertReady(williams);
        TestHelpers.AssertApproximately(-100m, williams.Value, 0.01m);
    }

    [Fact]
    public void WilliamsR_CloseAtMidpoint()
    {
        // Arrange
        var williams = Indicators.WilliamsR(5);

        // Act - Close at midpoint of range
        for (int i = 0; i < 5; i++)
        {
            // Highest = 110, Lowest = 100
            // Close = 105 (midpoint)
            // Williams %R = -100 * (110 - 105) / (110 - 100) = -100 * 5 / 10 = -50
            williams.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(williams);
        TestHelpers.AssertApproximately(-50m, williams.Value, 0.1m);
    }

    [Fact]
    public void WilliamsR_ResetsCorrectly()
    {
        // Arrange
        var williams = Indicators.WilliamsR(14);
        for (int i = 0; i < 20; i++)
        {
            williams.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Act
        williams.Reset();

        // Assert
        TestHelpers.AssertNotReady(williams);
        TestHelpers.AssertCount(0, williams);
    }

    [Fact]
    public void WilliamsR_OverboughtCondition()
    {
        // Arrange
        var williams = Indicators.WilliamsR(14);

        // Act - Strong uptrend, closes near highs
        for (int i = 0; i < 20; i++)
        {
            var low = 100m + i * 2;
            var high = low + 10m;
            var close = high - 0.5m; // Close near high
            williams.Update(TestHelpers.CreateBar(low, high, low, close, 1000m));
        }

        // Assert - Should be near 0 (overbought, -20 to 0 range)
        TestHelpers.AssertReady(williams);
        Assert.True(williams.Value > -20m, $"Expected overbought (> -20), got {williams.Value}");
    }

    [Fact]
    public void WilliamsR_OversoldCondition()
    {
        // Arrange
        var williams = Indicators.WilliamsR(14);

        // Act - Strong downtrend, closes near lows
        for (int i = 0; i < 20; i++)
        {
            var high = 200m - i * 2;
            var low = high - 10m;
            var close = low + 0.5m; // Close near low
            williams.Update(TestHelpers.CreateBar(low, high, low, close, 1000m));
        }

        // Assert - Should be near -100 (oversold, -100 to -80 range)
        TestHelpers.AssertReady(williams);
        Assert.True(williams.Value < -80m, $"Expected oversold (< -80), got {williams.Value}");
    }

    [Fact]
    public void WilliamsR_CountIncrementsCorrectly()
    {
        // Arrange
        var williams = Indicators.WilliamsR(14);

        // Act & Assert
        Assert.Equal(0, williams.Count);

        williams.Update(TestHelpers.CreateBar(100m));
        Assert.Equal(1, williams.Count);

        williams.Update(TestHelpers.CreateBar(105m));
        Assert.Equal(2, williams.Count);
    }

    [Fact]
    public void WilliamsR_ThrowsOnInvalidPeriod()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Indicators.WilliamsR(0));
        Assert.Throws<ArgumentException>(() => Indicators.WilliamsR(-1));
    }

    [Fact]
    public void WilliamsR_RollingWindow()
    {
        // Arrange
        var williams = Indicators.WilliamsR(5);

        // Act - Start with range 100-110
        for (int i = 0; i < 10; i++)
        {
            williams.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));
        }
        var value1 = williams.Value;

        // New higher range 110-120
        for (int i = 0; i < 5; i++)
        {
            williams.Update(TestHelpers.CreateBar(110m, 120m, 110m, 120m, 1000m));
        }
        var value2 = williams.Value;

        // Assert - Window should shift, affecting the calculation
        Assert.NotEqual(value1, value2);
    }

    [Fact]
    public void WilliamsR_WithBullishBars()
    {
        // Arrange
        var williams = Indicators.WilliamsR(10);

        // Act - Strong bullish bars
        for (int i = 0; i < 15; i++)
        {
            williams.Update(TestHelpers.CreateBullishBar(100m + i * 3, 110m + i * 3, volume: 1000m));
        }

        // Assert - Should indicate overbought
        TestHelpers.AssertReady(williams);
        Assert.True(williams.Value > -50m, $"Bullish trend should produce high Williams %R, got {williams.Value}");
    }

    [Fact]
    public void WilliamsR_WithBearishBars()
    {
        // Arrange
        var williams = Indicators.WilliamsR(10);

        // Act - Strong bearish bars
        for (int i = 0; i < 15; i++)
        {
            williams.Update(TestHelpers.CreateBearishBar(200m - i * 3, 190m - i * 3, volume: 1000m));
        }

        // Assert - Should indicate oversold
        TestHelpers.AssertReady(williams);
        Assert.True(williams.Value < -50m, $"Bearish trend should produce low Williams %R, got {williams.Value}");
    }

    [Fact]
    public void WilliamsR_ManualCalculation()
    {
        // Arrange
        var williams = Indicators.WilliamsR(3);

        // Manual calculation:
        // Bar 1: H=110, L=100, C=105
        // Bar 2: H=115, L=105, C=110
        // Bar 3: H=120, L=110, C=118

        // Highest high = 120
        // Lowest low = 100
        // Current close = 118
        // Williams %R = -100 * (120 - 118) / (120 - 100) = -100 * 2 / 20 = -10

        var bar1 = TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m);
        var bar2 = TestHelpers.CreateBar(105m, 115m, 105m, 110m, 1000m);
        var bar3 = TestHelpers.CreateBar(110m, 120m, 110m, 118m, 1000m);

        // Act
        williams.Update(bar1);
        williams.Update(bar2);
        williams.Update(bar3);

        // Assert
        TestHelpers.AssertReady(williams);
        TestHelpers.AssertApproximately(-10m, williams.Value, 0.1m);
    }

    [Fact]
    public void WilliamsR_ShortPeriod()
    {
        // Arrange
        var williams = Indicators.WilliamsR(3);

        // Act
        williams.Update(TestHelpers.CreateBar(100m, 110m, 100m, 110m, 1000m));
        williams.Update(TestHelpers.CreateBar(105m, 115m, 105m, 115m, 1000m));
        williams.Update(TestHelpers.CreateBar(110m, 120m, 110m, 120m, 1000m));

        // Assert - Close at highest high
        TestHelpers.AssertReady(williams);
        TestHelpers.AssertApproximately(0m, williams.Value, 0.01m);
    }

    [Fact]
    public void WilliamsR_WithConstantPrices()
    {
        // Arrange
        var williams = Indicators.WilliamsR(10);

        // Act - All bars at same price
        for (int i = 0; i < 15; i++)
        {
            williams.Update(TestHelpers.CreateBar(100m, 100m, 100m, 100m, 1000m));
        }

        // Assert - Range = 0, should return -50 (default)
        TestHelpers.AssertReady(williams);
        TestHelpers.AssertApproximately(-50m, williams.Value, 0.01m);
    }

    [Fact]
    public void WilliamsR_OscillatingPrices()
    {
        // Arrange
        var williams = Indicators.WilliamsR(5);

        // Act - Oscillate between high and low closes
        for (int i = 0; i < 10; i++)
        {
            if (i % 2 == 0)
            {
                williams.Update(TestHelpers.CreateBar(100m, 110m, 100m, 110m, 1000m)); // High
            }
            else
            {
                williams.Update(TestHelpers.CreateBar(100m, 110m, 100m, 100m, 1000m)); // Low
            }
        }

        // Assert - Should be somewhere in middle range
        TestHelpers.AssertReady(williams);
        TestHelpers.AssertInRange(williams.Value, -100m, 0m);
    }

    [Fact]
    public void WilliamsR_SensitiveToRecentPrices()
    {
        // Arrange
        var williams = Indicators.WilliamsR(5);

        // Act - Start with low range
        for (int i = 0; i < 5; i++)
        {
            williams.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));
        }
        var value1 = williams.Value;

        // Add new high
        williams.Update(TestHelpers.CreateBar(100m, 150m, 100m, 140m, 1000m));
        var value2 = williams.Value;

        // Assert - New high should change the calculation significantly
        Assert.NotEqual(value1, value2);
    }

    [Fact]
    public void WilliamsR_LongPeriod()
    {
        // Arrange
        var williams = Indicators.WilliamsR(50);

        // Act
        for (int i = 0; i < 60; i++)
        {
            williams.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(williams);
        TestHelpers.AssertInRange(williams.Value, -100m, 0m);
    }

    [Fact]
    public void WilliamsR_AsymmetricRange()
    {
        // Arrange
        var williams = Indicators.WilliamsR(5);

        // Act - Create a range with close at 25% from high
        // Highest = 120, Lowest = 100, Range = 20
        // Close = 115 (5 from high, 15 from low)
        // Williams %R = -100 * 5 / 20 = -25
        for (int i = 0; i < 5; i++)
        {
            var bar = TestHelpers.CreateBar(100m, 120m, 100m, 115m, 1000m);
            williams.Update(bar);
        }

        // Assert
        TestHelpers.AssertReady(williams);
        TestHelpers.AssertApproximately(-25m, williams.Value, 0.1m);
    }

    [Fact]
    public void WilliamsR_BreakoutScenario()
    {
        // Arrange
        var williams = Indicators.WilliamsR(10);

        // Act - Sideways then breakout
        for (int i = 0; i < 15; i++)
        {
            williams.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));
        }

        // Breakout to new high
        williams.Update(TestHelpers.CreateBar(110m, 150m, 110m, 148m, 2000m));

        // Assert - After breakout, close should be near new high
        TestHelpers.AssertReady(williams);
        Assert.True(williams.Value > -20m, "Breakout should show overbought condition");
    }
}
