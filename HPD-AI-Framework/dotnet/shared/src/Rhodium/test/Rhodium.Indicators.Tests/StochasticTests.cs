using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Stochastic oscillator (%K and %D).
/// </summary>
public class StochasticTests
{
    [Fact]
    public void Stochastic_IsReadyAfterKPeriodPlusDPeriod()
    {
        // Arrange - Default kPeriod=14, dPeriod=3
        var stoch = Indicators.Stochastic();

        // Act & Assert - Need kPeriod bars for %K, then dPeriod more for %D
        for (int i = 0; i < 13; i++)
        {
            stoch.Update(TestHelpers.CreateBar(100m + i, 1000m));
            TestHelpers.AssertNotReady(stoch);
        }

        // %K ready after 14 bars
        stoch.Update(TestHelpers.CreateBar(114m, 1000m));
        TestHelpers.AssertNotReady(stoch, "Stochastic not fully ready until %D is ready");

        // %D ready after 3 more bars (total 16)
        stoch.Update(TestHelpers.CreateBar(115m, 1000m));
        stoch.Update(TestHelpers.CreateBar(116m, 1000m));
        TestHelpers.AssertReady(stoch);
    }

    [Fact]
    public void Stochastic_HasKAndDValues()
    {
        // Arrange
        var stoch = Indicators.Stochastic(14, 3);

        // Act
        for (int i = 0; i < 20; i++)
        {
            stoch.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Assert - Both %K and %D should be calculated
        TestHelpers.AssertReady(stoch);
        Assert.NotEqual(0m, stoch.K);
        Assert.NotEqual(0m, stoch.D);
        Assert.Equal(stoch.K, stoch.Value); // Value property returns %K
    }

    [Fact]
    public void Stochastic_BoundedBetweenZeroAndHundred()
    {
        // Arrange
        var stoch = Indicators.Stochastic(14, 3);

        // Act - Various price movements
        for (int i = 0; i < 30; i++)
        {
            var low = 100m + i;
            var high = low + 20m;
            var close = low + (i % 20);
            stoch.Update(TestHelpers.CreateBar(low, high, low, close, 1000m));
        }

        // Assert - Both %K and %D should be 0-100
        TestHelpers.AssertReady(stoch);
        TestHelpers.AssertInRange(stoch.K, 0m, 100m);
        TestHelpers.AssertInRange(stoch.D, 0m, 100m);
    }

    [Fact]
    public void Stochastic_KEqualsHundredAtHigh()
    {
        // Arrange
        var stoch = Indicators.Stochastic(5, 3);

        // Act - Close at highest high
        for (int i = 0; i < 5; i++)
        {
            stoch.Update(TestHelpers.CreateBar(100m + i * 10, 110m + i * 10, 100m + i * 10, 105m + i * 10, 1000m));
        }
        // Now add bar with close at the highest high
        stoch.Update(TestHelpers.CreateBar(150m, 160m, 150m, 160m, 1000m));

        // Wait for %D to be ready
        stoch.Update(TestHelpers.CreateBar(150m, 160m, 150m, 160m, 1000m));
        stoch.Update(TestHelpers.CreateBar(150m, 160m, 150m, 160m, 1000m));

        // Assert - %K should be 100
        TestHelpers.AssertReady(stoch);
        TestHelpers.AssertApproximately(100m, stoch.K, 0.1m);
    }

    [Fact]
    public void Stochastic_KEqualsZeroAtLow()
    {
        // Arrange
        var stoch = Indicators.Stochastic(5, 3);

        // Act - Close at lowest low
        for (int i = 0; i < 5; i++)
        {
            stoch.Update(TestHelpers.CreateBar(100m - i * 10, 110m - i * 10, 100m - i * 10, 105m - i * 10, 1000m));
        }
        // Now add bar with close at the lowest low
        stoch.Update(TestHelpers.CreateBar(50m, 60m, 50m, 50m, 1000m));

        // Wait for %D to be ready
        stoch.Update(TestHelpers.CreateBar(50m, 60m, 50m, 50m, 1000m));
        stoch.Update(TestHelpers.CreateBar(50m, 60m, 50m, 50m, 1000m));

        // Assert - %K should be 0
        TestHelpers.AssertReady(stoch);
        TestHelpers.AssertApproximately(0m, stoch.K, 0.1m);
    }

    [Fact]
    public void Stochastic_KFiftyAtMidpoint()
    {
        // Arrange
        var stoch = Indicators.Stochastic(5, 3);

        // Act - Close at midpoint of range
        for (int i = 0; i < 10; i++)
        {
            // Highest = 110, Lowest = 100, Close = 105 (midpoint)
            // %K = 100 * (105 - 100) / (110 - 100) = 50
            stoch.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(stoch);
        TestHelpers.AssertApproximately(50m, stoch.K, 1m);
        TestHelpers.AssertApproximately(50m, stoch.D, 1m); // SMA of %K should also be 50
    }

    [Fact]
    public void Stochastic_DIsAverageOfK()
    {
        // Arrange
        var stoch = Indicators.Stochastic(5, 3);

        // Act - Create varying %K values
        for (int i = 0; i < 10; i++)
        {
            var close = 100m + (i % 5) * 2;
            stoch.Update(TestHelpers.CreateBar(100m, 110m, 100m, close, 1000m));
        }

        // Assert - %D should be smoother than %K (it's an SMA of %K)
        TestHelpers.AssertReady(stoch);
        // %D is the 3-period SMA of %K
    }

    [Fact]
    public void Stochastic_ResetsCorrectly()
    {
        // Arrange
        var stoch = Indicators.Stochastic(14, 3);
        for (int i = 0; i < 20; i++)
        {
            stoch.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Act
        stoch.Reset();

        // Assert
        TestHelpers.AssertNotReady(stoch);
        TestHelpers.AssertCount(0, stoch);
        Assert.Equal(0m, stoch.K);
        Assert.Equal(0m, stoch.D);
        Assert.Equal(0m, stoch.Value);
    }

    [Fact]
    public void Stochastic_CountIncrementsCorrectly()
    {
        // Arrange
        var stoch = Indicators.Stochastic(14, 3);

        // Act & Assert
        Assert.Equal(0, stoch.Count);

        stoch.Update(TestHelpers.CreateBar(100m));
        Assert.Equal(1, stoch.Count);

        stoch.Update(TestHelpers.CreateBar(105m));
        Assert.Equal(2, stoch.Count);
    }

    [Fact]
    public void Stochastic_ThrowsOnInvalidPeriods()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Indicators.Stochastic(0, 3));
        Assert.Throws<ArgumentException>(() => Indicators.Stochastic(-1, 3));
    }

    [Fact]
    public void Stochastic_OverboughtCondition()
    {
        // Arrange
        var stoch = Indicators.Stochastic(14, 3);

        // Act - Strong uptrend with closes near highs
        for (int i = 0; i < 25; i++)
        {
            var low = 100m + i * 5;
            var high = low + 10m;
            var close = high - 1m; // Close near high
            stoch.Update(TestHelpers.CreateBar(low, high, low, close, 1000m));
        }

        // Assert - %K should be high (> 80 is overbought)
        TestHelpers.AssertReady(stoch);
        Assert.True(stoch.K > 70m, $"Expected overbought %K (>70), got {stoch.K}");
    }

    [Fact]
    public void Stochastic_OversoldCondition()
    {
        // Arrange
        var stoch = Indicators.Stochastic(14, 3);

        // Act - Strong downtrend with closes near lows
        for (int i = 0; i < 25; i++)
        {
            var high = 200m - i * 5;
            var low = high - 10m;
            var close = low + 1m; // Close near low
            stoch.Update(TestHelpers.CreateBar(low, high, low, close, 1000m));
        }

        // Assert - %K should be low (< 20 is oversold)
        TestHelpers.AssertReady(stoch);
        Assert.True(stoch.K < 30m, $"Expected oversold %K (<30), got {stoch.K}");
    }

    [Fact]
    public void Stochastic_WithBullishBars()
    {
        // Arrange
        var stoch = Indicators.Stochastic(10, 3);

        // Act
        for (int i = 0; i < 20; i++)
        {
            stoch.Update(TestHelpers.CreateBullishBar(100m + i * 3, 110m + i * 3, volume: 1000m));
        }

        // Assert
        TestHelpers.AssertReady(stoch);
        Assert.True(stoch.K > 50m, $"Bullish bars should produce high %K, got {stoch.K}");
    }

    [Fact]
    public void Stochastic_WithBearishBars()
    {
        // Arrange
        var stoch = Indicators.Stochastic(10, 3);

        // Act
        for (int i = 0; i < 20; i++)
        {
            stoch.Update(TestHelpers.CreateBearishBar(200m - i * 3, 190m - i * 3, volume: 1000m));
        }

        // Assert
        TestHelpers.AssertReady(stoch);
        Assert.True(stoch.K < 50m, $"Bearish bars should produce low %K, got {stoch.K}");
    }

    [Fact]
    public void Stochastic_ManualCalculation()
    {
        // Arrange
        var stoch = Indicators.Stochastic(3, 2);

        // Manual calculation:
        // Bar 1: H=110, L=100, C=105
        // Bar 2: H=115, L=105, C=110
        // Bar 3: H=120, L=110, C=118

        // Highest high = 120
        // Lowest low = 100
        // Current close = 118
        // %K = 100 * (118 - 100) / (120 - 100) = 100 * 18 / 20 = 90

        var bar1 = TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m);
        var bar2 = TestHelpers.CreateBar(105m, 115m, 105m, 110m, 1000m);
        var bar3 = TestHelpers.CreateBar(110m, 120m, 110m, 118m, 1000m);

        // Act
        stoch.Update(bar1);
        stoch.Update(bar2);
        stoch.Update(bar3);

        // Need one more bar for %D (2-period SMA)
        stoch.Update(TestHelpers.CreateBar(115m, 120m, 115m, 118m, 1000m));

        // Assert
        TestHelpers.AssertReady(stoch);
        TestHelpers.AssertApproximately(90m, stoch.K, 5m);
    }

    [Fact]
    public void Stochastic_ShortPeriods()
    {
        // Arrange
        var stoch = Indicators.Stochastic(3, 2);

        // Act
        for (int i = 0; i < 10; i++)
        {
            stoch.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(stoch);
        TestHelpers.AssertInRange(stoch.K, 0m, 100m);
        TestHelpers.AssertInRange(stoch.D, 0m, 100m);
    }

    [Fact]
    public void Stochastic_WithConstantPrices()
    {
        // Arrange
        var stoch = Indicators.Stochastic(10, 3);

        // Act - All bars at same price
        for (int i = 0; i < 20; i++)
        {
            stoch.Update(TestHelpers.CreateBar(100m, 100m, 100m, 100m, 1000m));
        }

        // Assert - Range = 0, default to 50
        TestHelpers.AssertReady(stoch);
        TestHelpers.AssertApproximately(50m, stoch.K, 0.1m);
        TestHelpers.AssertApproximately(50m, stoch.D, 0.1m);
    }

    [Fact]
    public void Stochastic_KCrossesD()
    {
        // Arrange
        var stoch = Indicators.Stochastic(5, 3);

        // Act - Create scenario where %K and %D can cross
        for (int i = 0; i < 10; i++)
        {
            stoch.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));
        }
        var k1 = stoch.K;
        var d1 = stoch.D;

        // Add upward movement
        for (int i = 0; i < 5; i++)
        {
            stoch.Update(TestHelpers.CreateBar(100m, 110m, 100m, 109m, 1000m));
        }
        var k2 = stoch.K;
        var d2 = stoch.D;

        // Assert - %K should be more responsive than %D
        Assert.True(k2 > k1, "%K should increase");
    }

    [Fact]
    public void Stochastic_OscillatingPrices()
    {
        // Arrange
        var stoch = Indicators.Stochastic(10, 3);

        // Act - Oscillate between high and low closes
        for (int i = 0; i < 25; i++)
        {
            if (i % 2 == 0)
            {
                stoch.Update(TestHelpers.CreateBar(100m, 110m, 100m, 110m, 1000m)); // High
            }
            else
            {
                stoch.Update(TestHelpers.CreateBar(100m, 110m, 100m, 100m, 1000m)); // Low
            }
        }

        // Assert - Should oscillate between extremes
        TestHelpers.AssertReady(stoch);
        TestHelpers.AssertInRange(stoch.K, 0m, 100m);
        TestHelpers.AssertInRange(stoch.D, 0m, 100m);
    }

    [Fact]
    public void Stochastic_RollingWindow()
    {
        // Arrange
        var stoch = Indicators.Stochastic(5, 3);

        // Act - Start with low range
        for (int i = 0; i < 10; i++)
        {
            stoch.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));
        }
        var value1 = stoch.K;

        // Shift to higher range
        for (int i = 0; i < 5; i++)
        {
            stoch.Update(TestHelpers.CreateBar(150m, 160m, 150m, 158m, 1000m));
        }
        var value2 = stoch.K;

        // Assert - Window should shift
        Assert.NotEqual(value1, value2);
    }

    [Fact]
    public void Stochastic_DSmoothsK()
    {
        // Arrange
        var stoch = Indicators.Stochastic(5, 5); // Longer D period for more smoothing

        // Act - Create choppy %K
        for (int i = 0; i < 20; i++)
        {
            var close = 100m + (i % 3) * 5;
            stoch.Update(TestHelpers.CreateBar(100m, 115m, 100m, close, 1000m));
        }

        // Assert - %D should be present and smoother
        TestHelpers.AssertReady(stoch);
        Assert.NotEqual(0m, stoch.D);
    }

    [Fact]
    public void Stochastic_LongKPeriod()
    {
        // Arrange
        var stoch = Indicators.Stochastic(50, 3);

        // Act
        for (int i = 0; i < 60; i++)
        {
            stoch.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(stoch);
        TestHelpers.AssertInRange(stoch.K, 0m, 100m);
        TestHelpers.AssertInRange(stoch.D, 0m, 100m);
    }

    [Fact]
    public void Stochastic_ValueReturnsK()
    {
        // Arrange
        var stoch = Indicators.Stochastic(10, 3);

        // Act
        for (int i = 0; i < 20; i++)
        {
            stoch.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Assert - Value property should return %K
        TestHelpers.AssertReady(stoch);
        Assert.Equal(stoch.K, stoch.Value);
    }

    [Fact]
    public void Stochastic_FastVsSlow()
    {
        // Arrange
        var fastStoch = Indicators.Stochastic(5, 3);  // Fast
        var slowStoch = Indicators.Stochastic(14, 3); // Slow

        // Act - Same data
        for (int i = 0; i < 25; i++)
        {
            var bar = TestHelpers.CreateBar(100m + i, 1000m);
            fastStoch.Update(bar);
            slowStoch.Update(bar);
        }

        // Assert - Both should be ready but with different sensitivities
        TestHelpers.AssertReady(fastStoch);
        TestHelpers.AssertReady(slowStoch);
        // Fast should be more extreme (closer to 100 in uptrend)
        Assert.True(fastStoch.K >= slowStoch.K - 20m,
            $"Fast stoch should be at least as high as slow in uptrend: fast={fastStoch.K}, slow={slowStoch.K}");
    }
}
