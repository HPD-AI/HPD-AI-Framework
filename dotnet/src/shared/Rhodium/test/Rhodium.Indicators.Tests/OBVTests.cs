using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for On Balance Volume (OBV) indicator.
/// </summary>
public class OBVTests
{
    [Fact]
    public void OBV_StartsAtZero()
    {
        // Arrange
        var obv = Indicators.OBV();

        // Act
        obv.Update(TestHelpers.CreateBar(100m, 1000m));

        // Assert - First bar initializes, OBV = 0
        Assert.Equal(0m, obv.Value);
    }

    [Fact]
    public void OBV_AddsVolumeOnPriceIncrease()
    {
        // Arrange
        var obv = Indicators.OBV();

        // Act
        obv.Update(TestHelpers.CreateBar(100m, 1000m)); // Initialize
        obv.Update(TestHelpers.CreateBar(110m, 2000m)); // Price up

        // Assert - OBV should add volume: 0 + 2000 = 2000
        Assert.Equal(2000m, obv.Value);
    }

    [Fact]
    public void OBV_SubtractsVolumeOnPriceDecrease()
    {
        // Arrange
        var obv = Indicators.OBV();

        // Act
        obv.Update(TestHelpers.CreateBar(100m, 1000m)); // Initialize
        obv.Update(TestHelpers.CreateBar(90m, 2000m));  // Price down

        // Assert - OBV should subtract volume: 0 - 2000 = -2000
        Assert.Equal(-2000m, obv.Value);
    }

    [Fact]
    public void OBV_NoChangeOnSamePrice()
    {
        // Arrange
        var obv = Indicators.OBV();

        // Act
        obv.Update(TestHelpers.CreateBar(100m, 1000m)); // Initialize
        obv.Update(TestHelpers.CreateBar(100m, 2000m)); // Same price

        // Assert - OBV should remain 0
        Assert.Equal(0m, obv.Value);
    }

    [Fact]
    public void OBV_IsReadyAfterSecondBar()
    {
        // Arrange
        var obv = Indicators.OBV();

        // Act & Assert
        TestHelpers.AssertNotReady(obv);

        obv.Update(TestHelpers.CreateBar(100m));
        TestHelpers.AssertNotReady(obv, "OBV should not be ready after first bar");

        obv.Update(TestHelpers.CreateBar(110m));
        TestHelpers.AssertReady(obv, "OBV should be ready after second bar");
    }

    [Fact]
    public void OBV_ResetsCorrectly()
    {
        // Arrange
        var obv = Indicators.OBV();
        obv.Update(TestHelpers.CreateBar(100m, 1000m));
        obv.Update(TestHelpers.CreateBar(110m, 2000m));
        obv.Update(TestHelpers.CreateBar(120m, 3000m));

        // Act
        obv.Reset();

        // Assert
        TestHelpers.AssertNotReady(obv);
        TestHelpers.AssertCount(0, obv);
        Assert.Equal(0m, obv.Value);
    }

    [Fact]
    public void OBV_AccumulatesCorrectly()
    {
        // Arrange
        var obv = Indicators.OBV();

        // Act
        obv.Update(TestHelpers.CreateBar(100m, 1000m)); // Initialize, OBV = 0
        obv.Update(TestHelpers.CreateBar(110m, 1000m)); // Up, OBV = 0 + 1000 = 1000
        obv.Update(TestHelpers.CreateBar(120m, 1500m)); // Up, OBV = 1000 + 1500 = 2500
        obv.Update(TestHelpers.CreateBar(115m, 2000m)); // Down, OBV = 2500 - 2000 = 500
        obv.Update(TestHelpers.CreateBar(125m, 1000m)); // Up, OBV = 500 + 1000 = 1500

        // Assert
        Assert.Equal(1500m, obv.Value);
    }

    [Fact]
    public void OBV_RespondsToVolumeChanges()
    {
        // Arrange
        var obv1 = Indicators.OBV();
        var obv2 = Indicators.OBV();

        // Act - Same price movements, different volumes
        obv1.Update(TestHelpers.CreateBar(100m, 1000m));
        obv1.Update(TestHelpers.CreateBar(110m, 1000m)); // Small volume

        obv2.Update(TestHelpers.CreateBar(100m, 1000m));
        obv2.Update(TestHelpers.CreateBar(110m, 10000m)); // Large volume

        // Assert
        Assert.True(obv2.Value > obv1.Value,
            $"OBV with larger volume ({obv2.Value}) should be greater than smaller volume ({obv1.Value})");
    }

    [Fact]
    public void OBV_WithUptrend()
    {
        // Arrange
        var obv = Indicators.OBV();

        // Act - Consistent uptrend
        obv.Update(TestHelpers.CreateBar(100m, 1000m));
        obv.Update(TestHelpers.CreateBar(105m, 1000m));
        obv.Update(TestHelpers.CreateBar(110m, 1000m));
        obv.Update(TestHelpers.CreateBar(115m, 1000m));
        obv.Update(TestHelpers.CreateBar(120m, 1000m));

        // Assert - OBV should be strongly positive
        Assert.Equal(4000m, obv.Value); // 4 up moves * 1000 volume
    }

    [Fact]
    public void OBV_WithDowntrend()
    {
        // Arrange
        var obv = Indicators.OBV();

        // Act - Consistent downtrend
        obv.Update(TestHelpers.CreateBar(120m, 1000m));
        obv.Update(TestHelpers.CreateBar(115m, 1000m));
        obv.Update(TestHelpers.CreateBar(110m, 1000m));
        obv.Update(TestHelpers.CreateBar(105m, 1000m));
        obv.Update(TestHelpers.CreateBar(100m, 1000m));

        // Assert - OBV should be strongly negative
        Assert.Equal(-4000m, obv.Value); // 4 down moves * 1000 volume
    }

    [Fact]
    public void OBV_WithBullishBars()
    {
        // Arrange
        var obv = Indicators.OBV();

        // Act
        obv.Update(TestHelpers.CreateBullishBar(100m, 110m, volume: 1000m));
        obv.Update(TestHelpers.CreateBullishBar(110m, 120m, volume: 1500m));

        // Assert - Should accumulate volume
        Assert.Equal(1500m, obv.Value);
    }

    [Fact]
    public void OBV_WithBearishBars()
    {
        // Arrange
        var obv = Indicators.OBV();

        // Act
        obv.Update(TestHelpers.CreateBearishBar(120m, 110m, volume: 1000m));
        obv.Update(TestHelpers.CreateBearishBar(110m, 100m, volume: 1500m));

        // Assert - Should subtract volume
        Assert.Equal(-1500m, obv.Value);
    }

    [Fact]
    public void OBV_CountIncrementsCorrectly()
    {
        // Arrange
        var obv = Indicators.OBV();

        // Act & Assert
        Assert.Equal(0, obv.Count);

        obv.Update(TestHelpers.CreateBar(100m));
        Assert.Equal(1, obv.Count);

        obv.Update(TestHelpers.CreateBar(110m));
        Assert.Equal(2, obv.Count);

        obv.Update(TestHelpers.CreateBar(120m));
        Assert.Equal(3, obv.Count);
    }

    [Fact]
    public void OBV_HandlesZeroVolume()
    {
        // Arrange
        var obv = Indicators.OBV();

        // Act
        obv.Update(TestHelpers.CreateBar(100m, 1000m));
        obv.Update(TestHelpers.CreateBar(110m, 0m)); // Zero volume up move
        obv.Update(TestHelpers.CreateBar(105m, 1000m)); // Normal volume

        // Assert - Zero volume should not affect OBV, then subtract 1000
        Assert.Equal(-1000m, obv.Value);
    }

    [Fact]
    public void OBV_OscillatingPrices()
    {
        // Arrange
        var obv = Indicators.OBV();

        // Act - Up, down, up, down pattern
        obv.Update(TestHelpers.CreateBar(100m, 1000m)); // Init
        obv.Update(TestHelpers.CreateBar(110m, 1000m)); // +1000 = 1000
        obv.Update(TestHelpers.CreateBar(105m, 1000m)); // -1000 = 0
        obv.Update(TestHelpers.CreateBar(115m, 1000m)); // +1000 = 1000
        obv.Update(TestHelpers.CreateBar(110m, 1000m)); // -1000 = 0

        // Assert - Should return to zero
        Assert.Equal(0m, obv.Value);
    }

    [Fact]
    public void OBV_LargeVolumeSpike()
    {
        // Arrange
        var obv = Indicators.OBV();

        // Act
        obv.Update(TestHelpers.CreateBar(100m, 1000m));
        obv.Update(TestHelpers.CreateBar(105m, 1000m));   // +1000
        obv.Update(TestHelpers.CreateBar(110m, 100000m)); // Large volume spike

        // Assert
        Assert.Equal(101000m, obv.Value);
    }

    [Fact]
    public void OBV_CanGoNegative()
    {
        // Arrange
        var obv = Indicators.OBV();

        // Act - More selling than buying
        obv.Update(TestHelpers.CreateBar(100m, 1000m));
        obv.Update(TestHelpers.CreateBar(95m, 5000m)); // Strong sell

        // Assert
        Assert.Equal(-5000m, obv.Value);
        Assert.True(obv.Value < 0m);
    }
}
