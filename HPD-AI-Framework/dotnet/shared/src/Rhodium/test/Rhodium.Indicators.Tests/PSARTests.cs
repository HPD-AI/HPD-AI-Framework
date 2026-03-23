using Rhodium.Primitives;
using Rhodium.Indicators;
using static Rhodium.Indicators.Tests.TestHelpers;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Parabolic SAR (Stop and Reverse) indicator.
/// Tracks trend direction and potential reversal points.
/// </summary>
public class PSARTests
{
    [Fact]
    public void PSAR_Constructor_ShouldInitializeWithDefaultParameters()
    {
        // Arrange & Act
        var psar = Indicators.PSAR();

        // Assert
        Assert.NotNull(psar);
        Assert.Equal(0, psar.Count);
        Assert.False(psar.IsReady);
        Assert.Equal(0m, psar.Value);
        Assert.False(psar.IsLong);
    }

    [Fact]
    public void PSAR_Constructor_ShouldAcceptCustomParameters()
    {
        // Arrange & Act
        var psar = Indicators.PSAR(0.01m, 0.01m, 0.1m);

        // Assert
        Assert.NotNull(psar);
        Assert.Equal(0, psar.Count);
        Assert.False(psar.IsReady);
    }

    [Fact]
    public void PSAR_ShouldBecomeReadyAfterTwoBars()
    {
        // Arrange
        var psar = Indicators.PSAR();
        var bars = CreateTrendBars(AscendingPrices(100m, 1m, 5));

        // Act & Assert
        psar.Update(bars[0]);
        AssertNotReady(psar, "PSAR should not be ready after 1 bar");

        psar.Update(bars[1]);
        AssertReady(psar, "PSAR should be ready after 2 bars");
    }

    [Fact]
    public void PSAR_Uptrend_ShouldIdentifyAsLong()
    {
        // Arrange
        var psar = Indicators.PSAR();
        var uptrend = CreateTrendBars(AscendingPrices(100m, 2m, 20), volatility: 0.01m);

        // Act
        UpdateBars(psar, uptrend);

        // Assert
        AssertReady(psar);
        Assert.True(psar.IsLong, "PSAR should identify uptrend as long");
        Assert.True(psar.Value > 0m, "PSAR value should be positive");
    }

    [Fact]
    public void PSAR_Downtrend_ShouldIdentifyAsShort()
    {
        // Arrange
        var psar = Indicators.PSAR();
        var downtrend = CreateTrendBars(DescendingPrices(200m, 2m, 20), volatility: 0.01m);

        // Act
        UpdateBars(psar, downtrend);

        // Assert
        AssertReady(psar);
        Assert.False(psar.IsLong, "PSAR should identify downtrend as short");
        Assert.True(psar.Value > 0m, "PSAR value should be positive");
    }

    [Fact]
    public void PSAR_TrendReversal_ShouldFlipIsLong()
    {
        // Arrange
        var psar = Indicators.PSAR();

        // Create uptrend then strong downtrend
        var uptrend = AscendingPrices(100m, 1m, 15);
        var downtrend = DescendingPrices(114m, 2m, 15);
        var combined = uptrend.Concat(downtrend).ToArray();
        var bars = CreateTrendBars(combined, volatility: 0.01m);

        // Act - Update with uptrend
        for (int i = 0; i < 15; i++)
        {
            psar.Update(bars[i]);
        }
        var wasLong = psar.IsLong;

        // Continue with downtrend
        for (int i = 15; i < bars.Length; i++)
        {
            psar.Update(bars[i]);
        }

        // Assert - Should have reversed
        Assert.True(wasLong, "Should start as long in uptrend");
        Assert.False(psar.IsLong, "Should flip to short in downtrend");
    }

    [Fact]
    public void PSAR_Value_ShouldAlwaysBePositive()
    {
        // Arrange
        var psar = Indicators.PSAR();
        var bars = CreateTrendBars(SineWavePrices(100m, 20m, 30), volatility: 0.02m);

        // Act
        foreach (var bar in bars)
        {
            psar.Update(bar);
            if (psar.IsReady)
            {
                Assert.True(psar.Value > 0m, "PSAR value should always be positive (uses absolute value)");
            }
        }
    }

    [Fact]
    public void PSAR_ExtremePoint_ShouldUpdate()
    {
        // Arrange
        var psar = Indicators.PSAR();
        var uptrend = CreateTrendBars(AscendingPrices(100m, 2m, 10), volatility: 0.01m);

        // Act
        UpdateBars(psar, uptrend);

        // Assert
        AssertReady(psar);
        Assert.True(psar.EP > 0m, "Extreme Point should be set");
    }

    [Fact]
    public void PSAR_AccelerationFactor_ShouldIncreaseWithTrend()
    {
        // Arrange
        var psar = Indicators.PSAR(0.02m, 0.02m, 0.2m);
        var strongUptrend = CreateTrendBars(AscendingPrices(100m, 3m, 15), volatility: 0.005m);

        // Act
        psar.Update(strongUptrend[0]);
        psar.Update(strongUptrend[1]);
        var initialAF = psar.AF;

        for (int i = 2; i < strongUptrend.Length; i++)
        {
            psar.Update(strongUptrend[i]);
        }
        var laterAF = psar.AF;

        // Assert
        Assert.True(laterAF >= initialAF, "AF should increase or stay same during trend");
        Assert.True(laterAF <= 0.2m, "AF should not exceed max");
    }

    [Fact]
    public void PSAR_AccelerationFactor_ShouldNotExceedMax()
    {
        // Arrange
        var psar = Indicators.PSAR(0.02m, 0.05m, 0.15m);
        var veryLongTrend = CreateTrendBars(AscendingPrices(100m, 2m, 50), volatility: 0.005m);

        // Act
        UpdateBars(psar, veryLongTrend);

        // Assert
        Assert.True(psar.AF <= 0.15m, "AF should never exceed specified maximum");
    }

    [Fact]
    public void PSAR_Reset_ShouldClearAllState()
    {
        // Arrange
        var psar = Indicators.PSAR();
        var bars = CreateTrendBars(AscendingPrices(100m, 1m, 10));
        UpdateBars(psar, bars);

        // Act
        psar.Reset();

        // Assert
        AssertCount(0, psar);
        AssertNotReady(psar);
        Assert.Equal(0m, psar.Value);
        Assert.False(psar.IsLong);
        Assert.Equal(0m, psar.EP);
    }

    [Fact]
    public void PSAR_SARValue_ShouldTrailPrice()
    {
        // Arrange
        var psar = Indicators.PSAR();
        var uptrend = CreateTrendBars(AscendingPrices(100m, 2m, 15), volatility: 0.01m);

        // Act
        UpdateBars(psar, uptrend);

        // Assert
        AssertReady(psar);
        var lastBar = uptrend[uptrend.Length - 1];
        if (psar.IsLong)
        {
            Assert.True(psar.Value < lastBar.Low.Value, "In uptrend, SAR should be below current low");
        }
        else
        {
            Assert.True(psar.Value > lastBar.High.Value, "In downtrend, SAR should be above current high");
        }
    }

    [Fact]
    public void PSAR_MultipleReversals_ShouldHandleCorrectly()
    {
        // Arrange
        var psar = Indicators.PSAR();
        var oscillating = CreateTrendBars(OscillatingPrices(95m, 105m, 30), volatility: 0.05m);

        // Act
        UpdateBars(psar, oscillating);

        // Assert - Should handle reversals without errors
        AssertReady(psar);
        Assert.True(psar.Value > 0m, "PSAR should remain valid through multiple reversals");
    }

    [Fact]
    public void PSAR_DifferentAFSettings_ShouldProduceDifferentSensitivity()
    {
        // Arrange
        var psarSlow = Indicators.PSAR(0.01m, 0.01m, 0.1m);
        var psarFast = Indicators.PSAR(0.05m, 0.05m, 0.3m);
        var bars = CreateTrendBars(AscendingPrices(100m, 2m, 20), volatility: 0.02m);

        // Act
        UpdateBars(psarSlow, bars);
        UpdateBars(psarFast, bars);

        // Assert
        AssertReady(psarSlow);
        AssertReady(psarFast);
        Assert.True(psarSlow.AF != psarFast.AF || psarSlow.Value != psarFast.Value,
            "Different AF settings should produce different behavior");
    }

    [Fact]
    public void PSAR_ConstantPrices_ShouldMaintainDirection()
    {
        // Arrange
        var psar = Indicators.PSAR();
        var constantBars = CreateBars(ConstantPrices(100m, 15));

        // Act
        UpdateBars(psar, constantBars);

        // Assert
        AssertReady(psar);
        // With constant prices, should maintain initial direction
        Assert.True(psar.Value > 0m, "PSAR should have valid value with constant prices");
    }

    [Fact]
    public void PSAR_SmallPriceMovements_ShouldNotReverseFrequently()
    {
        // Arrange
        var psar = Indicators.PSAR();
        var smallMoves = CreateTrendBars(AscendingPrices(100m, 0.1m, 20), volatility: 0.005m);

        // Act
        UpdateBars(psar, smallMoves);

        // Assert
        AssertReady(psar);
        // Should maintain trend with small movements
        Assert.True(psar.IsLong, "Should maintain long position with small upward movements");
    }

    [Fact]
    public void PSAR_LargePriceMovements_ShouldReverseQuickly()
    {
        // Arrange
        var psar = Indicators.PSAR();

        // Small uptrend then large drop
        var uptrend = AscendingPrices(100m, 0.5m, 10);
        var largeDrop = DescendingPrices(104m, 5m, 5);
        var combined = uptrend.Concat(largeDrop).ToArray();
        var bars = CreateTrendBars(combined, volatility: 0.01m);

        // Act
        for (int i = 0; i < 10; i++)
        {
            psar.Update(bars[i]);
        }
        Assert.True(psar.IsLong, "Should be long after uptrend");

        for (int i = 10; i < bars.Length; i++)
        {
            psar.Update(bars[i]);
        }

        // Assert - Should reverse on large drop
        Assert.False(psar.IsLong, "Should reverse to short on large price drop");
    }

    [Fact]
    public void PSAR_UpdateSequentially_ShouldMaintainCount()
    {
        // Arrange
        var psar = Indicators.PSAR();
        var bars = CreateTrendBars(AscendingPrices(100m, 1m, 15));

        // Act
        foreach (var bar in bars)
        {
            var countBefore = psar.Count;
            psar.Update(bar);
            Assert.Equal(countBefore + 1, psar.Count);
        }

        // Assert
        AssertReady(psar);
        Assert.Equal(15, psar.Count);
    }

    [Fact]
    public void PSAR_AfterReset_ShouldReinitializeCorrectly()
    {
        // Arrange
        var psar = Indicators.PSAR();
        var bars = CreateTrendBars(AscendingPrices(100m, 1m, 15));
        UpdateBars(psar, bars);

        // Act
        psar.Reset();
        UpdateBars(psar, bars);

        // Assert - Should work the same after reset
        AssertReady(psar);
        Assert.True(psar.Value > 0m, "Should work correctly after reset");
    }

    [Fact]
    public void PSAR_WithGaps_ShouldDetectReversals()
    {
        // Arrange
        var psar = Indicators.PSAR();
        var bars = new[]
        {
            CreateBar(100m, 105m, 99m, 104m),
            CreateBar(104m, 108m, 103m, 107m),
            CreateBar(107m, 111m, 106m, 110m),
            CreateBar(110m, 114m, 109m, 113m),
            CreateBar(100m, 104m, 99m, 101m), // Large gap down - should trigger reversal
            CreateBar(101m, 103m, 98m, 99m),
        };

        // Act
        for (int i = 0; i < 4; i++)
        {
            psar.Update(bars[i]);
        }
        var beforeGap = psar.IsLong;

        psar.Update(bars[4]);
        psar.Update(bars[5]);

        // Assert
        Assert.True(beforeGap, "Should be long before gap");
        Assert.False(psar.IsLong, "Should reverse to short after large gap down");
    }

    [Fact]
    public void PSAR_EPProperty_ShouldReflectExtremePoint()
    {
        // Arrange
        var psar = Indicators.PSAR();
        var bars = CreateTrendBars(AscendingPrices(100m, 2m, 10), volatility: 0.01m);

        // Act
        UpdateBars(psar, bars);

        // Assert
        AssertReady(psar);
        if (psar.IsLong)
        {
            Assert.True(psar.EP >= bars[bars.Length - 1].High.Value * 0.9m,
                "EP should be near recent high in uptrend");
        }
    }

    [Fact]
    public void PSAR_InitialDirection_ShouldBeBasedOnFirstTwoBars()
    {
        // Arrange - First two bars show downtrend
        var psarDown = Indicators.PSAR();
        var downBars = new[]
        {
            CreateBar(100m, 105m, 99m, 103m),
            CreateBar(103m, 104m, 98m, 99m), // Lower close
        };

        // Act
        psarDown.Update(downBars[0]);
        psarDown.Update(downBars[1]);

        // Assert
        Assert.False(psarDown.IsLong, "Should initialize as short when second bar closes lower");

        // Arrange - First two bars show uptrend
        var psarUp = Indicators.PSAR();
        var upBars = new[]
        {
            CreateBar(100m, 105m, 99m, 103m),
            CreateBar(103m, 110m, 102m, 108m), // Higher close
        };

        // Act
        psarUp.Update(upBars[0]);
        psarUp.Update(upBars[1]);

        // Assert
        Assert.True(psarUp.IsLong, "Should initialize as long when second bar closes higher");
    }
}
