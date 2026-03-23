using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Chaikin Money Flow (CMF) indicator.
/// </summary>
public class CMFTests
{
    [Fact]
    public void CMF_IsReadyAfterPeriod()
    {
        // Arrange
        var cmf = Indicators.CMF(20);

        // Act & Assert
        for (int i = 0; i < 19; i++)
        {
            cmf.Update(TestHelpers.CreateBar(100m + i, 1000m));
            TestHelpers.AssertNotReady(cmf);
        }

        cmf.Update(TestHelpers.CreateBar(120m, 1000m));
        TestHelpers.AssertReady(cmf);
    }

    [Fact]
    public void CMF_BoundedBetweenMinusOneAndOne()
    {
        // Arrange
        var cmf = Indicators.CMF(10);

        // Act - Strong uptrend (closes near high)
        for (int i = 0; i < 20; i++)
        {
            var low = 100m + i * 5;
            var high = low + 10m;
            var close = high - 1m; // Close near high
            cmf.Update(TestHelpers.CreateBar(low, high, low, close, 1000m));
        }

        // Assert
        TestHelpers.AssertInRange(cmf.Value, -1m, 1m);
        Assert.True(cmf.Value > 0m, "Strong buying should produce positive CMF");

        // Act - Reset and strong downtrend
        cmf.Reset();
        for (int i = 0; i < 20; i++)
        {
            var low = 200m - i * 5;
            var high = low + 10m;
            var close = low + 1m; // Close near low
            cmf.Update(TestHelpers.CreateBar(low, high, low, close, 1000m));
        }

        // Assert
        TestHelpers.AssertInRange(cmf.Value, -1m, 1m);
        Assert.True(cmf.Value < 0m, "Strong selling should produce negative CMF");
    }

    [Fact]
    public void CMF_PositiveOnCloseNearHigh()
    {
        // Arrange
        var cmf = Indicators.CMF(10);

        // Act - Closes consistently near high
        for (int i = 0; i < 15; i++)
        {
            // High=110, Low=100, Close=109 -> Strong buying pressure
            cmf.Update(TestHelpers.CreateBar(100m, 110m, 100m, 109m, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(cmf);
        Assert.True(cmf.Value > 0.5m, $"Expected strongly positive CMF, got {cmf.Value}");
    }

    [Fact]
    public void CMF_NegativeOnCloseNearLow()
    {
        // Arrange
        var cmf = Indicators.CMF(10);

        // Act - Closes consistently near low
        for (int i = 0; i < 15; i++)
        {
            // High=110, Low=100, Close=101 -> Strong selling pressure
            cmf.Update(TestHelpers.CreateBar(100m, 110m, 100m, 101m, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(cmf);
        Assert.True(cmf.Value < -0.5m, $"Expected strongly negative CMF, got {cmf.Value}");
    }

    [Fact]
    public void CMF_NeutralOnCloseAtMidpoint()
    {
        // Arrange
        var cmf = Indicators.CMF(10);

        // Act - Closes consistently at midpoint
        for (int i = 0; i < 15; i++)
        {
            // High=110, Low=100, Close=105 -> Neutral
            cmf.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(cmf);
        TestHelpers.AssertApproximately(0m, cmf.Value, 0.01m);
    }

    [Fact]
    public void CMF_ResetsCorrectly()
    {
        // Arrange
        var cmf = Indicators.CMF(10);
        for (int i = 0; i < 15; i++)
        {
            cmf.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Act
        cmf.Reset();

        // Assert
        TestHelpers.AssertNotReady(cmf);
        TestHelpers.AssertCount(0, cmf);
        Assert.Equal(0m, cmf.Value);
    }

    [Fact]
    public void CMF_RespondsToVolumeChanges()
    {
        // Arrange
        var cmf1 = Indicators.CMF(5);
        var cmf2 = Indicators.CMF(5);

        // Act - Same close locations, different volumes
        for (int i = 0; i < 10; i++)
        {
            // Close near high
            cmf1.Update(TestHelpers.CreateBar(100m, 110m, 100m, 109m, 1000m));   // Normal volume
            cmf2.Update(TestHelpers.CreateBar(100m, 110m, 100m, 109m, 10000m));  // High volume
        }

        // Assert - Both should be positive
        Assert.True(cmf1.Value > 0m && cmf2.Value > 0m);
        TestHelpers.AssertReady(cmf1);
        TestHelpers.AssertReady(cmf2);
    }

    [Fact]
    public void CMF_CountIncrementsCorrectly()
    {
        // Arrange
        var cmf = Indicators.CMF(10);

        // Act & Assert
        Assert.Equal(0, cmf.Count);

        cmf.Update(TestHelpers.CreateBar(100m));
        Assert.Equal(1, cmf.Count);

        cmf.Update(TestHelpers.CreateBar(105m));
        Assert.Equal(2, cmf.Count);
    }

    [Fact]
    public void CMF_ThrowsOnInvalidPeriod()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Indicators.CMF(0));
        Assert.Throws<ArgumentException>(() => Indicators.CMF(-1));
    }

    [Fact]
    public void CMF_RollingWindow()
    {
        // Arrange
        var cmf = Indicators.CMF(5);

        // Act - Fill with neutral bars
        for (int i = 0; i < 10; i++)
        {
            cmf.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));
        }
        var neutralValue = cmf.Value;

        // Add strong buying bars
        for (int i = 0; i < 5; i++)
        {
            cmf.Update(TestHelpers.CreateBar(100m, 110m, 100m, 109m, 1000m));
        }
        var buyingValue = cmf.Value;

        // Assert - Should shift from neutral to positive
        TestHelpers.AssertApproximately(0m, neutralValue, 0.1m);
        Assert.True(buyingValue > neutralValue,
            $"CMF should increase with buying pressure: {neutralValue} -> {buyingValue}");
    }

    [Fact]
    public void CMF_WithBullishBars()
    {
        // Arrange
        var cmf = Indicators.CMF(10);

        // Act
        for (int i = 0; i < 15; i++)
        {
            cmf.Update(TestHelpers.CreateBullishBar(100m + i * 2, 110m + i * 2, volume: 1000m));
        }

        // Assert
        TestHelpers.AssertReady(cmf);
        Assert.True(cmf.Value > 0m, $"Expected positive CMF on bullish bars, got {cmf.Value}");
    }

    [Fact]
    public void CMF_WithBearishBars()
    {
        // Arrange
        var cmf = Indicators.CMF(10);

        // Act
        for (int i = 0; i < 15; i++)
        {
            cmf.Update(TestHelpers.CreateBearishBar(200m - i * 2, 190m - i * 2, volume: 1000m));
        }

        // Assert
        TestHelpers.AssertReady(cmf);
        Assert.True(cmf.Value < 0m, $"Expected negative CMF on bearish bars, got {cmf.Value}");
    }

    [Fact]
    public void CMF_WithZeroRange()
    {
        // Arrange
        var cmf = Indicators.CMF(5);

        // Act - Bars with no range
        for (int i = 0; i < 10; i++)
        {
            cmf.Update(TestHelpers.CreateBar(100m, 100m, 100m, 100m, 1000m));
        }

        // Assert - Should handle gracefully
        TestHelpers.AssertReady(cmf);
        Assert.Equal(0m, cmf.Value);
    }

    [Fact]
    public void CMF_ManualCalculation()
    {
        // Arrange
        var cmf = Indicators.CMF(3);

        // Manual calculation:
        // Bar 1: H=110, L=100, C=108, V=1000
        // CLV = ((108-100) - (110-108)) / 10 = 0.6
        // AD1 = 0.6 * 1000 = 600

        // Bar 2: H=115, L=105, C=112, V=1500
        // CLV = ((112-105) - (115-112)) / 10 = 0.4
        // AD2 = 0.4 * 1500 = 600

        // Bar 3: H=120, L=110, C=118, V=2000
        // CLV = ((118-110) - (120-118)) / 10 = 0.6
        // AD3 = 0.6 * 2000 = 1200

        // CMF = (600 + 600 + 1200) / (1000 + 1500 + 2000) = 2400 / 4500 = 0.5333

        var bar1 = TestHelpers.CreateBar(100m, 110m, 100m, 108m, 1000m);
        var bar2 = TestHelpers.CreateBar(105m, 115m, 105m, 112m, 1500m);
        var bar3 = TestHelpers.CreateBar(110m, 120m, 110m, 118m, 2000m);

        // Act
        cmf.Update(bar1);
        cmf.Update(bar2);
        cmf.Update(bar3);

        // Assert
        TestHelpers.AssertReady(cmf);
        TestHelpers.AssertApproximately(0.5333m, cmf.Value, 0.01m);
    }

    [Fact]
    public void CMF_ShortPeriod()
    {
        // Arrange
        var cmf = Indicators.CMF(3);

        // Act
        cmf.Update(TestHelpers.CreateBar(100m, 110m, 100m, 109m, 1000m));
        cmf.Update(TestHelpers.CreateBar(110m, 120m, 110m, 119m, 1000m));
        cmf.Update(TestHelpers.CreateBar(120m, 130m, 120m, 129m, 1000m));

        // Assert
        TestHelpers.AssertReady(cmf);
        Assert.True(cmf.Value > 0.5m, "Close near high should produce high CMF");
    }

    [Fact]
    public void CMF_WithZeroVolume()
    {
        // Arrange
        var cmf = Indicators.CMF(5);

        // Act
        for (int i = 0; i < 10; i++)
        {
            var volume = i % 2 == 0 ? 0m : 1000m; // Alternating zero volume
            cmf.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, volume));
        }

        // Assert - Should handle gracefully
        TestHelpers.AssertReady(cmf);
    }

    [Fact]
    public void CMF_LongPeriod()
    {
        // Arrange
        var cmf = Indicators.CMF(50);

        // Act - Need 50 bars to be ready
        for (int i = 0; i < 60; i++)
        {
            cmf.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(cmf);
        TestHelpers.AssertInRange(cmf.Value, -1m, 1m);
    }

    [Fact]
    public void CMF_OscillatingPattern()
    {
        // Arrange
        var cmf = Indicators.CMF(10);

        // Act - Alternating buying and selling pressure
        for (int i = 0; i < 20; i++)
        {
            if (i % 2 == 0)
            {
                // Close near high
                cmf.Update(TestHelpers.CreateBar(100m, 110m, 100m, 109m, 1000m));
            }
            else
            {
                // Close near low
                cmf.Update(TestHelpers.CreateBar(100m, 110m, 100m, 101m, 1000m));
            }
        }

        // Assert - Should be near neutral
        TestHelpers.AssertReady(cmf);
        TestHelpers.AssertInRange(cmf.Value, -0.3m, 0.3m);
    }
}
