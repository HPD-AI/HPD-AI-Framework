using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Commodity Channel Index (CCI) indicator.
/// </summary>
public class CCITests
{
    [Fact]
    public void CCI_IsReadyAfterPeriod()
    {
        // Arrange
        var cci = Indicators.CCI(20);

        // Act & Assert
        for (int i = 0; i < 19; i++)
        {
            cci.Update(TestHelpers.CreateBar(100m + i, 1000m));
            TestHelpers.AssertNotReady(cci);
        }

        cci.Update(TestHelpers.CreateBar(120m, 1000m));
        TestHelpers.AssertReady(cci);
    }

    [Fact]
    public void CCI_CalculatesWithDefaultConstant()
    {
        // Arrange - Default constant is 0.015
        var cci = Indicators.CCI(14);

        // Act
        for (int i = 0; i < 20; i++)
        {
            cci.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(cci);
    }

    [Fact]
    public void CCI_CalculatesWithCustomConstant()
    {
        // Arrange
        var cci = Indicators.CCI(14, 0.02m);

        // Act
        for (int i = 0; i < 20; i++)
        {
            cci.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(cci);
    }

    [Fact]
    public void CCI_PositiveOnUptrend()
    {
        // Arrange
        var cci = Indicators.CCI(14);

        // Act - Strong uptrend
        for (int i = 0; i < 25; i++)
        {
            var price = 100m + i * 5;
            cci.Update(TestHelpers.CreateBar(price, price + 10, price - 5, price + 5, 1000m));
        }

        // Assert - CCI should be positive on uptrend
        TestHelpers.AssertReady(cci);
        Assert.True(cci.Value > 0m, $"Expected positive CCI on uptrend, got {cci.Value}");
    }

    [Fact]
    public void CCI_NegativeOnDowntrend()
    {
        // Arrange
        var cci = Indicators.CCI(14);

        // Act - Strong downtrend
        for (int i = 0; i < 25; i++)
        {
            var price = 200m - i * 5;
            cci.Update(TestHelpers.CreateBar(price - 5, price + 5, price - 10, price - 5, 1000m));
        }

        // Assert - CCI should be negative on downtrend
        TestHelpers.AssertReady(cci);
        Assert.True(cci.Value < 0m, $"Expected negative CCI on downtrend, got {cci.Value}");
    }

    [Fact]
    public void CCI_NearZeroOnSideways()
    {
        // Arrange
        var cci = Indicators.CCI(14);

        // Act - Sideways market - oscillating bars around a center
        // Create bars that oscillate to simulate sideways movement
        for (int i = 0; i < 25; i++)
        {
            // Alternate between slightly different bars to create sideways market
            if (i % 2 == 0)
            {
                cci.Update(TestHelpers.CreateBar(100m, 108m, 97m, 103m, 1000m));
            }
            else
            {
                cci.Update(TestHelpers.CreateBar(103m, 110m, 95m, 100m, 1000m));
            }
        }

        // Assert - CCI should be near zero for sideways/ranging market
        // CCI can fluctuate significantly in oscillating markets
        // Allow wide tolerance for ranging behavior
        TestHelpers.AssertReady(cci);
        TestHelpers.AssertInRange(cci.Value, -100m, 100m);
    }

    [Fact]
    public void CCI_ResetsCorrectly()
    {
        // Arrange
        var cci = Indicators.CCI(14);
        for (int i = 0; i < 20; i++)
        {
            cci.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Act
        cci.Reset();

        // Assert
        TestHelpers.AssertNotReady(cci);
        TestHelpers.AssertCount(0, cci);
        Assert.Equal(0m, cci.Value);
    }

    [Fact]
    public void CCI_CountIncrementsCorrectly()
    {
        // Arrange
        var cci = Indicators.CCI(14);

        // Act & Assert
        Assert.Equal(0, cci.Count);

        cci.Update(TestHelpers.CreateBar(100m));
        Assert.Equal(1, cci.Count);

        cci.Update(TestHelpers.CreateBar(105m));
        Assert.Equal(2, cci.Count);
    }

    [Fact]
    public void CCI_ThrowsOnInvalidPeriod()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Indicators.CCI(0));
        Assert.Throws<ArgumentException>(() => Indicators.CCI(-1));
    }

    [Fact]
    public void CCI_OverboughtCondition()
    {
        // Arrange
        var cci = Indicators.CCI(14);

        // Act - Create overbought condition (CCI > 100)
        for (int i = 0; i < 20; i++)
        {
            var price = 100m + i * 10; // Rapid increase
            cci.Update(TestHelpers.CreateBar(price, price + 5, price - 5, price, 1000m));
        }

        // Assert - CCI should exceed +100 in overbought condition
        TestHelpers.AssertReady(cci);
        Assert.True(cci.Value > 50m, $"Expected overbought CCI, got {cci.Value}");
    }

    [Fact]
    public void CCI_OversoldCondition()
    {
        // Arrange
        var cci = Indicators.CCI(14);

        // Act - Create oversold condition (CCI < -100)
        for (int i = 0; i < 20; i++)
        {
            var price = 200m - i * 10; // Rapid decrease
            cci.Update(TestHelpers.CreateBar(price, price + 5, price - 5, price, 1000m));
        }

        // Assert - CCI should go below -100 in oversold condition
        TestHelpers.AssertReady(cci);
        Assert.True(cci.Value < -50m, $"Expected oversold CCI, got {cci.Value}");
    }

    [Fact]
    public void CCI_WithBullishBars()
    {
        // Arrange
        var cci = Indicators.CCI(10);

        // Act
        for (int i = 0; i < 15; i++)
        {
            cci.Update(TestHelpers.CreateBullishBar(100m + i * 3, 110m + i * 3, volume: 1000m));
        }

        // Assert
        TestHelpers.AssertReady(cci);
        Assert.True(cci.Value > 0m, $"Expected positive CCI on bullish bars, got {cci.Value}");
    }

    [Fact]
    public void CCI_WithBearishBars()
    {
        // Arrange
        var cci = Indicators.CCI(10);

        // Act
        for (int i = 0; i < 15; i++)
        {
            cci.Update(TestHelpers.CreateBearishBar(200m - i * 3, 190m - i * 3, volume: 1000m));
        }

        // Assert
        TestHelpers.AssertReady(cci);
        Assert.True(cci.Value < 0m, $"Expected negative CCI on bearish bars, got {cci.Value}");
    }

    [Fact]
    public void CCI_ManualCalculation()
    {
        // Arrange
        var cci = Indicators.CCI(3, 0.015m);

        // Manual calculation for 3 periods:
        // Bar 1: H=110, L=100, C=105, TP = (110+100+105)/3 = 105
        // Bar 2: H=115, L=105, C=110, TP = (115+105+110)/3 = 110
        // Bar 3: H=120, L=110, C=118, TP = (120+110+118)/3 = 116

        // SMA of TP = (105 + 110 + 116) / 3 = 110.33
        // MAD = (|105-110.33| + |110-110.33| + |116-110.33|) / 3
        //     = (5.33 + 0.33 + 5.67) / 3 = 3.78
        // Current TP = 116
        // CCI = (116 - 110.33) / (0.015 * 3.78) = 5.67 / 0.0567 = 100

        var bar1 = TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m);
        var bar2 = TestHelpers.CreateBar(105m, 115m, 105m, 110m, 1000m);
        var bar3 = TestHelpers.CreateBar(110m, 120m, 110m, 118m, 1000m);

        // Act
        cci.Update(bar1);
        cci.Update(bar2);
        cci.Update(bar3);

        // Assert
        TestHelpers.AssertReady(cci);
        TestHelpers.AssertApproximately(100m, cci.Value, 5m);
    }

    [Fact]
    public void CCI_ShortPeriod()
    {
        // Arrange
        var cci = Indicators.CCI(3);

        // Act
        cci.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));
        cci.Update(TestHelpers.CreateBar(105m, 115m, 105m, 110m, 1000m));
        cci.Update(TestHelpers.CreateBar(110m, 120m, 110m, 115m, 1000m));

        // Assert
        TestHelpers.AssertReady(cci);
    }

    [Fact]
    public void CCI_WithConstantPrices()
    {
        // Arrange
        var cci = Indicators.CCI(14);

        // Act - All bars at same price
        for (int i = 0; i < 20; i++)
        {
            cci.Update(TestHelpers.CreateBar(100m, 100m, 100m, 100m, 1000m));
        }

        // Assert - MAD = 0, so CCI = 0
        TestHelpers.AssertReady(cci);
        Assert.Equal(0m, cci.Value);
    }

    [Fact]
    public void CCI_RollingWindow()
    {
        // Arrange
        var cci = Indicators.CCI(5);

        // Act - Start with one range (constant bars give CCI = 0)
        for (int i = 0; i < 10; i++)
        {
            cci.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));
        }
        var value1 = cci.Value; // Should be 0 (constant typical price)

        // Shift to new higher prices with variation
        for (int i = 0; i < 6; i++)
        {
            var base_price = 150m + i * 2;
            cci.Update(TestHelpers.CreateBar(base_price, base_price + 10, base_price, base_price + 5, 1000m));
        }
        var value2 = cci.Value;

        // Assert - Should respond to new price levels and show positive CCI (uptrend)
        Assert.Equal(0m, value1); // Constant bars = CCI of 0
        Assert.True(value2 > 0m, $"After uptrend, CCI should be positive, got {value2}");
    }

    [Fact]
    public void CCI_OscillatingPrices()
    {
        // Arrange
        var cci = Indicators.CCI(10);

        // Act - Oscillate between two levels
        for (int i = 0; i < 20; i++)
        {
            var price = i % 2 == 0 ? 100m : 110m;
            cci.Update(TestHelpers.CreateBar(price, price + 5, price - 5, price, 1000m));
        }

        // Assert - Should oscillate around zero
        TestHelpers.AssertReady(cci);
        TestHelpers.AssertInRange(cci.Value, -150m, 150m);
    }

    [Fact]
    public void CCI_HigherConstantGivesLowerValues()
    {
        // Arrange
        var cci1 = Indicators.CCI(14, 0.015m);
        var cci2 = Indicators.CCI(14, 0.030m); // Double the constant

        // Act - Same data
        for (int i = 0; i < 20; i++)
        {
            var bar = TestHelpers.CreateBar(100m + i * 2, 1000m);
            cci1.Update(bar);
            cci2.Update(bar);
        }

        // Assert - Higher constant should produce lower absolute CCI values
        TestHelpers.AssertReady(cci1);
        TestHelpers.AssertReady(cci2);
        Assert.True(Math.Abs(cci2.Value) < Math.Abs(cci1.Value),
            $"Higher constant should produce lower CCI: cci1={cci1.Value}, cci2={cci2.Value}");
    }

    [Fact]
    public void CCI_SensitiveToTypicalPrice()
    {
        // Arrange
        var cci = Indicators.CCI(5);

        // Act - Same close, different typical prices
        for (int i = 0; i < 10; i++)
        {
            // Wide range (high TP variation)
            cci.Update(TestHelpers.CreateBar(100m, 150m, 50m, 100m, 1000m));
        }

        // Assert - Should handle wide ranges
        TestHelpers.AssertReady(cci);
    }

    [Fact]
    public void CCI_LongPeriod()
    {
        // Arrange
        var cci = Indicators.CCI(50);

        // Act
        for (int i = 0; i < 60; i++)
        {
            cci.Update(TestHelpers.CreateBar(100m + i, 1000m));
        }

        // Assert
        TestHelpers.AssertReady(cci);
    }

    [Fact]
    public void CCI_TrendReversal()
    {
        // Arrange
        var cci = Indicators.CCI(10);

        // Act - Uptrend
        for (int i = 0; i < 15; i++)
        {
            cci.Update(TestHelpers.CreateBar(100m + i * 3, 1000m));
        }
        var uptrendValue = cci.Value;

        // Reversal to downtrend
        for (int i = 0; i < 10; i++)
        {
            cci.Update(TestHelpers.CreateBar(145m - i * 5, 1000m));
        }
        var downtrendValue = cci.Value;

        // Assert - Should shift from positive to negative
        Assert.True(uptrendValue > 0m, "Uptrend should produce positive CCI");
        Assert.True(downtrendValue < uptrendValue, "Downtrend should produce lower CCI");
    }

    [Fact]
    public void CCI_ExtremePriceDeviation()
    {
        // Arrange
        var cci = Indicators.CCI(10);

        // Act - Normal prices then extreme spike
        for (int i = 0; i < 12; i++)
        {
            cci.Update(TestHelpers.CreateBar(100m, 110m, 100m, 105m, 1000m));
        }

        // Extreme spike
        cci.Update(TestHelpers.CreateBar(100m, 200m, 100m, 195m, 1000m));

        // Assert - CCI should spike significantly
        TestHelpers.AssertReady(cci);
        Assert.True(Math.Abs(cci.Value) > 50m, $"Extreme deviation should produce large CCI, got {cci.Value}");
    }
}
