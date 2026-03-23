using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Accumulation/Distribution (AD) indicator.
/// </summary>
public class ADTests
{
    [Fact]
    public void AD_IsReadyAfterFirstBar()
    {
        // Arrange
        var ad = Indicators.AD();

        // Act & Assert
        TestHelpers.AssertNotReady(ad);

        ad.Update(TestHelpers.CreateBar(100m, 110m, 90m, 105m, 1000m));
        TestHelpers.AssertReady(ad);
    }

    [Fact]
    public void AD_CalculatesCorrectly_CloseAtHigh()
    {
        // Arrange
        var ad = Indicators.AD();

        // Act - Close at high (strong buying)
        // High=110, Low=100, Close=110
        // CLV = ((110-100) - (110-110)) / (110-100) = 10/10 = 1.0
        // AD = 1.0 * 1000 = 1000
        ad.Update(TestHelpers.CreateBar(100m, 110m, 100m, 110m, 1000m));

        // Assert
        Assert.Equal(1000m, ad.Value);
    }

    [Fact]
    public void AD_CalculatesCorrectly_CloseAtLow()
    {
        // Arrange
        var ad = Indicators.AD();

        // Act - Close at low (strong selling)
        // High=110, Low=100, Close=100
        // CLV = ((100-100) - (110-100)) / (110-100) = -10/10 = -1.0
        // AD = -1.0 * 1000 = -1000
        ad.Update(TestHelpers.CreateBar(110m, 110m, 100m, 100m, 1000m));

        // Assert
        Assert.Equal(-1000m, ad.Value);
    }

    [Fact]
    public void AD_CalculatesCorrectly_CloseAtMidpoint()
    {
        // Arrange
        var ad = Indicators.AD();

        // Act - Close at midpoint (neutral)
        // High=110, Low=100, Close=105
        // CLV = ((105-100) - (110-105)) / (110-100) = (5-5)/10 = 0
        // AD = 0 * 1000 = 0
        ad.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));

        // Assert
        Assert.Equal(0m, ad.Value);
    }

    [Fact]
    public void AD_Accumulates()
    {
        // Arrange
        var ad = Indicators.AD();

        // Act
        // Bar 1: High=110, Low=100, Close=108, Volume=1000
        // CLV = ((108-100) - (110-108)) / 10 = (8-2)/10 = 0.6
        // AD = 0.6 * 1000 = 600
        ad.Update(TestHelpers.CreateBar(100m, 110m, 100m, 108m, 1000m));
        var ad1 = ad.Value;

        // Bar 2: High=115, Low=105, Close=113, Volume=1500
        // CLV = ((113-105) - (115-113)) / 10 = (8-2)/10 = 0.6
        // AD = 600 + (0.6 * 1500) = 600 + 900 = 1500
        ad.Update(TestHelpers.CreateBar(105m, 115m, 105m, 113m, 1500m));
        var ad2 = ad.Value;

        // Assert
        TestHelpers.AssertApproximately(600m, ad1, 0.01m);
        TestHelpers.AssertApproximately(1500m, ad2, 0.01m);
    }

    [Fact]
    public void AD_ResetsCorrectly()
    {
        // Arrange
        var ad = Indicators.AD();
        ad.Update(TestHelpers.CreateBar(100m, 110m, 90m, 105m, 1000m));
        ad.Update(TestHelpers.CreateBar(105m, 115m, 100m, 110m, 1500m));

        // Act
        ad.Reset();

        // Assert
        TestHelpers.AssertNotReady(ad);
        TestHelpers.AssertCount(0, ad);
        Assert.Equal(0m, ad.Value);
    }

    [Fact]
    public void AD_RespondsToVolumeChanges()
    {
        // Arrange
        var ad1 = Indicators.AD();
        var ad2 = Indicators.AD();

        // Same CLV, different volumes
        var bar1 = TestHelpers.CreateBar(100m, 110m, 100m, 108m, 1000m);   // Low volume
        var bar2 = TestHelpers.CreateBar(100m, 110m, 100m, 108m, 10000m);  // High volume

        // Act
        ad1.Update(bar1);
        ad2.Update(bar2);

        // Assert
        Assert.True(ad2.Value > ad1.Value,
            $"AD with higher volume ({ad2.Value}) should be greater than lower volume ({ad1.Value})");
    }

    [Fact]
    public void AD_WithZeroRange()
    {
        // Arrange
        var ad = Indicators.AD();

        // Act - High = Low (no range)
        ad.Update(TestHelpers.CreateBar(100m, 100m, 100m, 100m, 1000m));

        // Assert - Should handle gracefully, CLV = 0
        Assert.Equal(0m, ad.Value);
    }

    [Fact]
    public void AD_CanGoNegative()
    {
        // Arrange
        var ad = Indicators.AD();

        // Act - Close at low (selling pressure)
        ad.Update(TestHelpers.CreateBar(110m, 110m, 100m, 100m, 2000m));

        // Assert
        Assert.Equal(-2000m, ad.Value);
        Assert.True(ad.Value < 0m);
    }

    [Fact]
    public void AD_WithBullishBars()
    {
        // Arrange
        var ad = Indicators.AD();

        // Act - Series of bullish bars
        ad.Update(TestHelpers.CreateBullishBar(100m, 110m, volume: 1000m));
        ad.Update(TestHelpers.CreateBullishBar(110m, 120m, volume: 1000m));
        ad.Update(TestHelpers.CreateBullishBar(120m, 130m, volume: 1000m));

        // Assert - Should be positive (accumulation)
        Assert.True(ad.Value > 0m, $"Expected positive AD on bullish bars, got {ad.Value}");
    }

    [Fact]
    public void AD_WithBearishBars()
    {
        // Arrange
        var ad = Indicators.AD();

        // Act - Series of bearish bars
        ad.Update(TestHelpers.CreateBearishBar(130m, 120m, volume: 1000m));
        ad.Update(TestHelpers.CreateBearishBar(120m, 110m, volume: 1000m));
        ad.Update(TestHelpers.CreateBearishBar(110m, 100m, volume: 1000m));

        // Assert - Should be negative (distribution)
        Assert.True(ad.Value < 0m, $"Expected negative AD on bearish bars, got {ad.Value}");
    }

    [Fact]
    public void AD_CountIncrementsCorrectly()
    {
        // Arrange
        var ad = Indicators.AD();

        // Act & Assert
        Assert.Equal(0, ad.Count);

        ad.Update(TestHelpers.CreateBar(100m, 110m, 90m, 105m));
        Assert.Equal(1, ad.Count);

        ad.Update(TestHelpers.CreateBar(105m, 115m, 100m, 110m));
        Assert.Equal(2, ad.Count);
    }

    [Fact]
    public void AD_ManualCalculation()
    {
        // Arrange
        var ad = Indicators.AD();

        // Manual calculation for comprehensive verification
        // Bar 1: O=100, H=110, L=95, C=105, V=2000
        // CLV = ((105-95) - (110-105)) / (110-95) = (10-5)/15 = 0.3333
        // AD1 = 0.3333 * 2000 = 666.67

        // Bar 2: O=105, H=115, L=100, C=112, V=3000
        // CLV = ((112-100) - (115-112)) / (115-100) = (12-3)/15 = 0.6
        // AD2 = 666.67 + (0.6 * 3000) = 666.67 + 1800 = 2466.67

        var bar1 = TestHelpers.CreateBar(100m, 110m, 95m, 105m, 2000m);
        var bar2 = TestHelpers.CreateBar(105m, 115m, 100m, 112m, 3000m);

        // Act
        ad.Update(bar1);
        var ad1 = ad.Value;

        ad.Update(bar2);
        var ad2 = ad.Value;

        // Assert
        TestHelpers.AssertApproximately(666.67m, ad1, 0.5m);
        TestHelpers.AssertApproximately(2466.67m, ad2, 0.5m);
    }

    [Fact]
    public void AD_OscillatingPrices()
    {
        // Arrange
        var ad = Indicators.AD();

        // Act - Alternating high close and low close
        ad.Update(TestHelpers.CreateBar(100m, 110m, 100m, 110m, 1000m)); // CLV = 1.0, AD = 1000
        ad.Update(TestHelpers.CreateBar(110m, 120m, 110m, 110m, 1000m)); // CLV = -1.0, AD = 0
        ad.Update(TestHelpers.CreateBar(110m, 120m, 110m, 120m, 1000m)); // CLV = 1.0, AD = 1000
        ad.Update(TestHelpers.CreateBar(120m, 130m, 120m, 120m, 1000m)); // CLV = -1.0, AD = 0

        // Assert
        Assert.Equal(0m, ad.Value);
    }

    [Fact]
    public void AD_WithZeroVolume()
    {
        // Arrange
        var ad = Indicators.AD();

        // Act
        ad.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 0m));
        ad.Update(TestHelpers.CreateBar(105m, 115m, 100m, 110m, 1000m));

        // Assert - Zero volume bar should contribute 0
        TestHelpers.AssertReady(ad);
    }

    [Fact]
    public void AD_StrongAccumulation()
    {
        // Arrange
        var ad = Indicators.AD();

        // Act - Series of closes near high with high volume
        for (int i = 0; i < 10; i++)
        {
            var low = 100m + i * 10;
            var high = low + 10m;
            var close = high - 1m; // Close near high
            ad.Update(TestHelpers.CreateBar(low, high, low, close, 1000m));
        }

        // Assert - Should show strong accumulation
        Assert.True(ad.Value > 5000m, $"Expected strong accumulation, got {ad.Value}");
    }

    [Fact]
    public void AD_StrongDistribution()
    {
        // Arrange
        var ad = Indicators.AD();

        // Act - Series of closes near low with high volume
        for (int i = 0; i < 10; i++)
        {
            var low = 100m - i * 10;
            var high = low + 10m;
            var close = low + 1m; // Close near low
            ad.Update(TestHelpers.CreateBar(low, high, low, close, 1000m));
        }

        // Assert - Should show strong distribution
        Assert.True(ad.Value < -5000m, $"Expected strong distribution, got {ad.Value}");
    }
}
