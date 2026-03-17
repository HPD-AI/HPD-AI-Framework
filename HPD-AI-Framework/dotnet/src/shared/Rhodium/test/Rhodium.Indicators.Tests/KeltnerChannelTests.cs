using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

public class KeltnerChannelTests
{
    [Fact]
    public void BasicFunctionality_CalculatesChannelFromEMAandATR()
    {
        var kc = Indicators.KeltnerChannel(10, 2m);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 20));

        TestHelpers.UpdateBars(kc, bars);

        TestHelpers.AssertReady(kc);

        // Middle should be the EMA
        Assert.True(kc.Middle > 0);

        // Upper and lower should be offset by ATR * multiplier
        Assert.True(kc.Upper > kc.Middle);
        Assert.True(kc.Lower < kc.Middle);
    }

    [Fact]
    public void BecomesReadyWhenEMAandATRReady()
    {
        var kc = Indicators.KeltnerChannel(14, 2m);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 20));

        // KeltnerChannel needs both EMA(14) and ATR(14) ready
        // EMA ready after 14 bars, ATR ready after 15 bars (period+1)
        // So KeltnerChannel ready after max(14, 15) = 15 bars
        for (int i = 0; i < 14; i++)
        {
            kc.Update(bars[i]);
            TestHelpers.AssertNotReady(kc, $"Should not be ready after {i + 1} bars");
        }

        kc.Update(bars[14]);
        TestHelpers.AssertReady(kc, "Should be ready after 15 bars");
    }

    [Fact]
    public void ResetClearsState()
    {
        var kc = Indicators.KeltnerChannel(10, 2m);

        TestHelpers.TestReset(kc, () =>
        {
            var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 15));
            TestHelpers.UpdateBars(kc, bars);
        });

        Assert.Equal(0m, kc.Upper);
        Assert.Equal(0m, kc.Middle);
        Assert.Equal(0m, kc.Lower);
    }

    [Fact]
    public void UpperAboveMiddleAboveLower()
    {
        var kc = Indicators.KeltnerChannel(10, 2m);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.OscillatingPrices(90m, 110m, 20));

        TestHelpers.UpdateBars(kc, bars);

        TestHelpers.AssertReady(kc);

        Assert.True(kc.Upper > kc.Middle);
        Assert.True(kc.Middle > kc.Lower);
    }

    [Fact]
    public void ValueEqualsMiddle()
    {
        var kc = Indicators.KeltnerChannel(10, 2m);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 15));

        TestHelpers.UpdateBars(kc, bars);

        TestHelpers.AssertReady(kc);
        Assert.Equal(kc.Middle, kc.Value);
    }

    [Fact]
    public void ChannelWidthMatchesATRTimesMultiplier()
    {
        var kc = Indicators.KeltnerChannel(10, 2m);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 20));

        TestHelpers.UpdateBars(kc, bars);

        TestHelpers.AssertReady(kc);

        var upperOffset = kc.Upper - kc.Middle;
        var lowerOffset = kc.Middle - kc.Lower;

        // Both offsets should be equal (symmetric channel)
        TestHelpers.AssertApproximately(upperOffset, lowerOffset, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void HigherMultiplierWidensChannel()
    {
        var kc1 = Indicators.KeltnerChannel(10, 1m);
        var kc2 = Indicators.KeltnerChannel(10, 2m);
        var kc3 = Indicators.KeltnerChannel(10, 3m);

        var bars = TestHelpers.CreateTrendBars(TestHelpers.OscillatingPrices(90m, 110m, 20));

        TestHelpers.UpdateBars(kc1, bars);
        TestHelpers.UpdateBars(kc2, bars);
        TestHelpers.UpdateBars(kc3, bars);

        TestHelpers.AssertReady(kc1);
        TestHelpers.AssertReady(kc2);
        TestHelpers.AssertReady(kc3);

        // Middle should be the same (same EMA)
        TestHelpers.AssertApproximately(kc1.Middle, kc2.Middle, 0.1m);
        TestHelpers.AssertApproximately(kc2.Middle, kc3.Middle, 0.1m);

        // Width should increase with multiplier
        var width1 = kc1.Upper - kc1.Lower;
        var width2 = kc2.Upper - kc2.Lower;
        var width3 = kc3.Upper - kc3.Lower;

        Assert.True(width2 > width1);
        Assert.True(width3 > width2);
    }

    [Fact]
    public void ChannelWidensWithVolatility()
    {
        var kc1 = Indicators.KeltnerChannel(10, 2m);
        var kc2 = Indicators.KeltnerChannel(10, 2m);

        // Low volatility
        var lowVolBars = new List<Bar>();
        for (int i = 0; i < 20; i++)
        {
            lowVolBars.Add(TestHelpers.CreateBar(100m, 101m, 99m, 100m));
        }
        TestHelpers.UpdateBars(kc1, lowVolBars.ToArray());

        // High volatility
        var highVolBars = new List<Bar>();
        for (int i = 0; i < 20; i++)
        {
            highVolBars.Add(TestHelpers.CreateBar(100m, 115m, 85m, 100m));
        }
        TestHelpers.UpdateBars(kc2, highVolBars.ToArray());

        TestHelpers.AssertReady(kc1);
        TestHelpers.AssertReady(kc2);

        var width1 = kc1.Upper - kc1.Lower;
        var width2 = kc2.Upper - kc2.Lower;

        Assert.True(width2 > width1);
    }

    [Fact]
    public void ChannelNarrowsWithConstantPrices()
    {
        var kc = Indicators.KeltnerChannel(10, 2m);
        var bars = TestHelpers.CreateBars(TestHelpers.ConstantPrices(100m, 20));

        TestHelpers.UpdateBars(kc, bars);

        TestHelpers.AssertReady(kc);

        // With constant prices, ATR should be 0, so bands should converge to middle
        var width = kc.Upper - kc.Lower;
        TestHelpers.AssertApproximately(0m, width, TestHelpers.DefaultPrecision);
        TestHelpers.AssertApproximately(100m, kc.Middle, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void DifferentPeriods()
    {
        var kc10 = Indicators.KeltnerChannel(10, 2m);
        var kc20 = Indicators.KeltnerChannel(20, 2m);

        var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 30));

        TestHelpers.UpdateBars(kc10, bars);
        TestHelpers.UpdateBars(kc20, bars);

        TestHelpers.AssertReady(kc10);
        TestHelpers.AssertReady(kc20);

        Assert.True(kc10.Middle > 0);
        Assert.True(kc20.Middle > 0);
    }

    [Fact]
    public void MiddleTracksEMA()
    {
        var kc = Indicators.KeltnerChannel(10, 2m);
        var ema = Indicators.EMA(10);

        var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 20));

        foreach (var bar in bars)
        {
            kc.Update(bar);
            ema.Update(bar.Close.Value);
        }

        TestHelpers.AssertReady(kc);
        TestHelpers.AssertReady(ema);

        // Middle should equal EMA
        TestHelpers.AssertApproximately(ema.Value, kc.Middle, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void SymmetricChannel()
    {
        var kc = Indicators.KeltnerChannel(10, 2m);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.OscillatingPrices(95m, 105m, 20));

        TestHelpers.UpdateBars(kc, bars);

        TestHelpers.AssertReady(kc);

        var upperDistance = kc.Upper - kc.Middle;
        var lowerDistance = kc.Middle - kc.Lower;

        // Channel should be symmetric
        TestHelpers.AssertApproximately(upperDistance, lowerDistance, TestHelpers.DefaultPrecision);
    }
}
