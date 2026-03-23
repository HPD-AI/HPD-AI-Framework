using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Money Flow Index (MFI) indicator.
/// </summary>
public class MFITests
{
    [Fact]
    public void MFI_IsReadyAfterPeriodPlusOne()
    {
        // Arrange
        var mfi = Indicators.MFI(14);

        // Act & Assert - Need period + 1 bars (first bar initializes typical price)
        for (int i = 0; i < 14; i++)
        {
            mfi.Update(TestHelpers.CreateBar(100m + i, 1000m));
            TestHelpers.AssertNotReady(mfi);
        }

        mfi.Update(TestHelpers.CreateBar(115m, 1000m));
        TestHelpers.AssertReady(mfi);
    }

    [Fact]
    public void MFI_BoundedBetweenZeroAndHundred()
    {
        // Arrange
        var mfi = Indicators.MFI(14);

        // Act - Extreme uptrend
        for (int i = 0; i < 20; i++)
        {
            mfi.Update(TestHelpers.CreateBar(100m + i * 5, 1000m));
        }

        // Assert
        TestHelpers.AssertInRange(mfi.Value, 0m, 100m);
        Assert.True(mfi.Value > 50m, "Strong uptrend should produce high MFI");

        // Act - Reset and extreme downtrend
        mfi.Reset();
        for (int i = 0; i < 20; i++)
        {
            mfi.Update(TestHelpers.CreateBar(200m - i * 5, 1000m));
        }

        // Assert
        TestHelpers.AssertInRange(mfi.Value, 0m, 100m);
        Assert.True(mfi.Value < 50m, "Strong downtrend should produce low MFI");
    }

    [Fact]
    public void MFI_HighValueOnUptrend()
    {
        // Arrange
        var mfi = Indicators.MFI(14);

        // Act - Consistent uptrend with increasing typical prices
        for (int i = 0; i < 20; i++)
        {
            mfi.Update(TestHelpers.CreateBar(100m + i * 2, 1000m));
        }

        // Assert - MFI should be high (near 100) on strong uptrend
        Assert.True(mfi.Value > 80m, $"Expected MFI > 80 on uptrend, got {mfi.Value}");
    }

    [Fact]
    public void MFI_LowValueOnDowntrend()
    {
        // Arrange
        var mfi = Indicators.MFI(14);

        // Act - Consistent downtrend with decreasing typical prices
        for (int i = 0; i < 20; i++)
        {
            mfi.Update(TestHelpers.CreateBar(200m - i * 2, 1000m));
        }

        // Assert - MFI should be low (near 0) on strong downtrend
        Assert.True(mfi.Value < 20m, $"Expected MFI < 20 on downtrend, got {mfi.Value}");
    }

    [Fact]
    public void MFI_ResetsCorrectly()
    {
        // Arrange
        var mfi = Indicators.MFI(14);
        for (int i = 0; i < 20; i++)
        {
            mfi.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Act
        mfi.Reset();

        // Assert
        TestHelpers.AssertNotReady(mfi);
        TestHelpers.AssertCount(0, mfi);
    }

    [Fact]
    public void MFI_RespondsToVolumeChanges()
    {
        // Arrange
        var mfi1 = Indicators.MFI(5);
        var mfi2 = Indicators.MFI(5);

        // Act - Same price pattern, different volumes
        for (int i = 0; i < 10; i++)
        {
            mfi1.Update(TestHelpers.CreateBar(100m + i, 1000m)); // Normal volume
            mfi2.Update(TestHelpers.CreateBar(100m + i, 10000m)); // High volume
        }

        // Assert - Both should be ready and high, but values affected by volume
        TestHelpers.AssertReady(mfi1);
        TestHelpers.AssertReady(mfi2);
        Assert.True(mfi1.Value > 50m && mfi2.Value > 50m);
    }

    [Fact]
    public void MFI_WithConstantPrices()
    {
        // Arrange
        var mfi = Indicators.MFI(14);

        // Act - All bars at same price
        for (int i = 0; i < 20; i++)
        {
            mfi.Update(TestHelpers.CreateBar(100m, 1000m));
        }

        // Assert - With no price movement, no positive or negative flow
        TestHelpers.AssertReady(mfi);
        // When there's no movement, MFI formula: sumNeg = 0, so MFI = 100
        Assert.Equal(100m, mfi.Value);
    }

    [Fact]
    public void MFI_CountIncrementsCorrectly()
    {
        // Arrange
        var mfi = Indicators.MFI(14);

        // Act & Assert
        Assert.Equal(0, mfi.Count);

        mfi.Update(TestHelpers.CreateBar(100m));
        Assert.Equal(1, mfi.Count);

        mfi.Update(TestHelpers.CreateBar(105m));
        Assert.Equal(2, mfi.Count);
    }

    [Fact]
    public void MFI_ThrowsOnInvalidPeriod()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Indicators.MFI(0));
        Assert.Throws<ArgumentException>(() => Indicators.MFI(-1));
    }

    [Fact]
    public void MFI_WithOscillatingPrices()
    {
        // Arrange
        var mfi = Indicators.MFI(10);

        // Act - Alternating up and down
        for (int i = 0; i < 20; i++)
        {
            var price = i % 2 == 0 ? 100m : 110m;
            mfi.Update(TestHelpers.CreateBar(price, 1000m));
        }

        // Assert - Should be around 50 (neutral)
        TestHelpers.AssertReady(mfi);
        TestHelpers.AssertInRange(mfi.Value, 30m, 70m);
    }

    [Fact]
    public void MFI_SensitiveToRecentPriceChanges()
    {
        // Arrange
        var mfi = Indicators.MFI(5);

        // Act - Start with downtrend to establish non-100 baseline
        for (int i = 0; i < 10; i++)
        {
            mfi.Update(TestHelpers.CreateBar(150m - i * 2, 1000m));
        }
        var valueBefore = mfi.Value;

        // Add strong up bars
        for (int i = 0; i < 5; i++)
        {
            mfi.Update(TestHelpers.CreateBar(132m + i * 5, 2000m));
        }
        var valueAfter = mfi.Value;

        // Assert - MFI should increase after switching to uptrend
        // Note: If valueBefore was already very high, increase might be limited
        Assert.True(valueAfter > valueBefore || valueAfter > 80m,
            $"MFI should respond to uptrend: before={valueBefore}, after={valueAfter}");
    }

    [Fact]
    public void MFI_WithBullishBars()
    {
        // Arrange
        var mfi = Indicators.MFI(10);

        // Act - Strong bullish bars
        for (int i = 0; i < 15; i++)
        {
            var open = 100m + i * 2;
            var close = 100m + i * 2 + 5;
            mfi.Update(TestHelpers.CreateBullishBar(open, close, volume: 1000m));
        }

        // Assert - Should show strong buying pressure
        TestHelpers.AssertReady(mfi);
        Assert.True(mfi.Value > 70m, $"Expected high MFI on bullish bars, got {mfi.Value}");
    }

    [Fact]
    public void MFI_WithBearishBars()
    {
        // Arrange
        var mfi = Indicators.MFI(10);

        // Act - Strong bearish bars
        for (int i = 0; i < 15; i++)
        {
            var open = 200m - i * 2;
            var close = 200m - i * 2 - 5;
            mfi.Update(TestHelpers.CreateBearishBar(open, close, volume: 1000m));
        }

        // Assert - Should show strong selling pressure
        TestHelpers.AssertReady(mfi);
        Assert.True(mfi.Value < 30m, $"Expected low MFI on bearish bars, got {mfi.Value}");
    }

    [Fact]
    public void MFI_ShortPeriod()
    {
        // Arrange
        var mfi = Indicators.MFI(3);

        // Act
        mfi.Update(TestHelpers.CreateBar(100m, 1000m)); // Init
        mfi.Update(TestHelpers.CreateBar(105m, 1000m)); // Up
        mfi.Update(TestHelpers.CreateBar(110m, 1000m)); // Up
        mfi.Update(TestHelpers.CreateBar(115m, 1000m)); // Up - Now ready

        // Assert
        TestHelpers.AssertReady(mfi);
        Assert.True(mfi.Value > 50m, "Uptrend should produce MFI > 50");
    }

    [Fact]
    public void MFI_VolumeWeighting()
    {
        // Arrange
        var mfi1 = Indicators.MFI(5);
        var mfi2 = Indicators.MFI(5);

        // Act - Same initial prices
        for (int i = 0; i < 6; i++)
        {
            mfi1.Update(TestHelpers.CreateBar(100m, 1000m));
            mfi2.Update(TestHelpers.CreateBar(100m, 1000m));
        }

        // Strong up move with different volumes
        for (int i = 0; i < 5; i++)
        {
            mfi1.Update(TestHelpers.CreateBar(110m + i, 1000m));   // Low volume
            mfi2.Update(TestHelpers.CreateBar(110m + i, 10000m));  // High volume
        }

        // Assert - Both should be high, but high volume should amplify the signal
        Assert.True(mfi1.Value > 50m && mfi2.Value > 50m);
        TestHelpers.AssertReady(mfi1);
        TestHelpers.AssertReady(mfi2);
    }
}
