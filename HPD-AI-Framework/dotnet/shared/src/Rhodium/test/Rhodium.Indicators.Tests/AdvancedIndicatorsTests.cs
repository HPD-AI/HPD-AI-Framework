using Rhodium.Primitives;
using Rhodium.Indicators;

namespace Rhodium.Indicators.Tests;

public class SwingHighTests
{
    [Fact]
    public void SwingHigh_DetectsSwingHigh()
    {
        var swingHigh = Indicators.SwingHigh(2, 2);

        // Create bars where middle bar (110) is a swing high
        var bars = new[]
        {
            TestHelpers.CreateBar(100m, 105m, 95m, 102m),
            TestHelpers.CreateBar(102m, 108m, 100m, 106m),
            TestHelpers.CreateBar(106m, 110m, 104m, 108m), // Swing high
            TestHelpers.CreateBar(108m, 109m, 102m, 105m),
            TestHelpers.CreateBar(105m, 107m, 100m, 103m)
        };

        foreach (var bar in bars)
            swingHigh.Update(bar);

        Assert.True(swingHigh.IsReady);
        Assert.True(swingHigh.IsSwing);
        Assert.Equal(110m, swingHigh.High);
    }

    [Fact]
    public void SwingHigh_NoSwing_WhenNotHighest()
    {
        var swingHigh = Indicators.SwingHigh(2, 2);

        var bars = new[]
        {
            TestHelpers.CreateBar(100m, 105m, 95m, 102m),
            TestHelpers.CreateBar(102m, 115m, 100m, 106m), // This is higher
            TestHelpers.CreateBar(106m, 110m, 104m, 108m),
            TestHelpers.CreateBar(108m, 109m, 102m, 105m),
            TestHelpers.CreateBar(105m, 107m, 100m, 103m)
        };

        foreach (var bar in bars)
            swingHigh.Update(bar);

        Assert.True(swingHigh.IsReady);
        Assert.False(swingHigh.IsSwing);
    }

    [Fact]
    public void SwingHigh_BecomesReady_AfterTotalBars()
    {
        var swingHigh = Indicators.SwingHigh(2, 2);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 10);
        var bars = TestHelpers.CreateBars(prices);

        for (int i = 0; i < 4; i++)
            swingHigh.Update(bars[i]);

        Assert.False(swingHigh.IsReady);

        swingHigh.Update(bars[4]);
        Assert.True(swingHigh.IsReady);
    }
}

public class SwingLowTests
{
    [Fact]
    public void SwingLow_DetectsSwingLow()
    {
        var swingLow = Indicators.SwingLow(2, 2);

        // Create bars where middle bar (95) is a swing low
        var bars = new[]
        {
            TestHelpers.CreateBar(100m, 105m, 98m, 102m),
            TestHelpers.CreateBar(102m, 108m, 100m, 106m),
            TestHelpers.CreateBar(106m, 110m, 95m, 108m), // Swing low
            TestHelpers.CreateBar(108m, 112m, 102m, 105m),
            TestHelpers.CreateBar(105m, 107m, 100m, 103m)
        };

        foreach (var bar in bars)
            swingLow.Update(bar);

        Assert.True(swingLow.IsReady);
        Assert.True(swingLow.IsSwing);
        Assert.Equal(95m, swingLow.Low);
    }

    [Fact]
    public void SwingLow_NoSwing_WhenNotLowest()
    {
        var swingLow = Indicators.SwingLow(2, 2);

        var bars = new[]
        {
            TestHelpers.CreateBar(100m, 105m, 90m, 102m), // This is lower
            TestHelpers.CreateBar(102m, 108m, 100m, 106m),
            TestHelpers.CreateBar(106m, 110m, 95m, 108m),
            TestHelpers.CreateBar(108m, 109m, 102m, 105m),
            TestHelpers.CreateBar(105m, 107m, 100m, 103m)
        };

        foreach (var bar in bars)
            swingLow.Update(bar);

        Assert.True(swingLow.IsReady);
        Assert.False(swingLow.IsSwing);
    }
}

public class AroonOscTests
{
    [Fact]
    public void AroonOsc_CalculatesCorrectValue()
    {
        var aroonOsc = Indicators.AroonOsc(5);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 10);
        var bars = TestHelpers.CreateBars(prices);

        foreach (var bar in bars)
            aroonOsc.Update(bar);

        Assert.True(aroonOsc.IsReady);
        // Uptrend should have positive oscillator (Up > Down)
        Assert.True(aroonOsc.Value > 0);
    }

    [Fact]
    public void AroonOsc_Downtrend_IsNegative()
    {
        var aroonOsc = Indicators.AroonOsc(5);
        var prices = TestHelpers.DescendingPrices(100m, 1m, 10);
        var bars = TestHelpers.CreateBars(prices);

        foreach (var bar in bars)
            aroonOsc.Update(bar);

        Assert.True(aroonOsc.IsReady);
        // Downtrend should have negative oscillator (Down > Up)
        Assert.True(aroonOsc.Value < 0);
    }

    [Fact]
    public void AroonOsc_EqualsUpMinusDown()
    {
        var aroonOsc = Indicators.AroonOsc(5);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 10);
        var bars = TestHelpers.CreateBars(prices);

        foreach (var bar in bars)
            aroonOsc.Update(bar);

        var expectedValue = aroonOsc.Up - aroonOsc.Down;
        TestHelpers.AssertApproximately(expectedValue, aroonOsc.Value);
    }
}

public class SuperTrendTests
{
    [Fact]
    public void SuperTrend_DetectsUptrend()
    {
        var superTrend = Indicators.SuperTrend(10, 3m);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 20);
        var bars = TestHelpers.CreateBars(prices);

        foreach (var bar in bars)
            superTrend.Update(bar);

        Assert.True(superTrend.IsReady);
        Assert.True(superTrend.IsUpTrend);
    }

    [Fact]
    public void SuperTrend_DetectsDowntrend()
    {
        var superTrend = Indicators.SuperTrend(10, 3m);
        var prices = TestHelpers.DescendingPrices(100m, 1m, 20);
        var bars = TestHelpers.CreateBars(prices);

        foreach (var bar in bars)
            superTrend.Update(bar);

        Assert.True(superTrend.IsReady);
        Assert.False(superTrend.IsUpTrend);
    }

    [Fact]
    public void SuperTrend_ValueEqualsCorrectBand()
    {
        var superTrend = Indicators.SuperTrend(10, 3m);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 20);
        var bars = TestHelpers.CreateBars(prices);

        foreach (var bar in bars)
            superTrend.Update(bar);

        Assert.True(superTrend.IsReady);
        // In uptrend, value should be lower band
        if (superTrend.IsUpTrend)
            Assert.Equal(superTrend.LowerBand, superTrend.Value);
    }

    [Fact]
    public void SuperTrend_BecomesReady_AfterPeriod()
    {
        var superTrend = Indicators.SuperTrend(10, 3m);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 15);
        var bars = TestHelpers.CreateBars(prices);

        // Update with period bars and verify it becomes ready
        foreach (var bar in bars)
            superTrend.Update(bar);

        Assert.True(superTrend.IsReady);
    }
}

public class IchimokuTests
{
    [Fact]
    public void Ichimoku_CalculatesAllComponents()
    {
        var ichimoku = Indicators.Ichimoku(9, 26, 52);
        var prices = TestHelpers.AscendingPrices(100m, 0.5m, 60);
        var bars = TestHelpers.CreateBars(prices);

        foreach (var bar in bars)
            ichimoku.Update(bar);

        Assert.True(ichimoku.IsReady);
        Assert.True(ichimoku.Tenkan > 0);
        Assert.True(ichimoku.Kijun > 0);
        Assert.True(ichimoku.SenkouA > 0);
        Assert.True(ichimoku.SenkouB > 0);
        Assert.True(ichimoku.Chikou > 0);
    }

    [Fact]
    public void Ichimoku_SenkouA_IsMidpointOfTenkanKijun()
    {
        var ichimoku = Indicators.Ichimoku(9, 26, 52);
        var prices = TestHelpers.AscendingPrices(100m, 0.5m, 60);
        var bars = TestHelpers.CreateBars(prices);

        foreach (var bar in bars)
            ichimoku.Update(bar);

        var expectedSenkouA = (ichimoku.Tenkan + ichimoku.Kijun) / 2;
        TestHelpers.AssertApproximately(expectedSenkouA, ichimoku.SenkouA, TestHelpers.LowPrecision);
    }

    [Fact]
    public void Ichimoku_Chikou_EqualsLastClose()
    {
        var ichimoku = Indicators.Ichimoku(9, 26, 52);
        var prices = TestHelpers.AscendingPrices(100m, 0.5m, 60);
        var bars = TestHelpers.CreateBars(prices);

        foreach (var bar in bars)
            ichimoku.Update(bar);

        Assert.Equal(bars[^1].Close.Value, ichimoku.Chikou);
    }

    [Fact]
    public void Ichimoku_BecomesReady_AfterSenkouBPeriod()
    {
        var ichimoku = Indicators.Ichimoku(9, 26, 52);
        var prices = TestHelpers.AscendingPrices(100m, 0.5m, 60);
        var bars = TestHelpers.CreateBars(prices);

        for (int i = 0; i < 51; i++)
        {
            ichimoku.Update(bars[i]);
            Assert.False(ichimoku.IsReady);
        }

        ichimoku.Update(bars[51]);
        Assert.True(ichimoku.IsReady);
    }
}

public class OrderBookAnalysisTests
{
    [Fact]
    public void BookImbalanceRatio_MoreBids_IsPositive()
    {
        var ratio = OrderBookAnalysis.BookImbalanceRatio(100m, 50m);
        Assert.True(ratio > 0, "More bid size should produce positive ratio");
        TestHelpers.AssertApproximately(0.333m, ratio, TestHelpers.LowPrecision);
    }

    [Fact]
    public void BookImbalanceRatio_MoreAsks_IsNegative()
    {
        var ratio = OrderBookAnalysis.BookImbalanceRatio(50m, 100m);
        Assert.True(ratio < 0, "More ask size should produce negative ratio");
        TestHelpers.AssertApproximately(-0.333m, ratio, TestHelpers.LowPrecision);
    }

    [Fact]
    public void BookImbalanceRatio_Equal_IsZero()
    {
        var ratio = OrderBookAnalysis.BookImbalanceRatio(100m, 100m);
        TestHelpers.AssertApproximately(0m, ratio);
    }

    [Fact]
    public void WeightedBookImbalance_CalculatesWithLevels()
    {
        var bids = new[] { (100m, 10m), (99m, 20m), (98m, 15m) };
        var asks = new[] { (101m, 5m), (102m, 10m), (103m, 8m) };

        var imbalance = OrderBookAnalysis.WeightedBookImbalance(bids, asks, 3);
        Assert.True(imbalance > 0, "More bid depth should produce positive imbalance");
    }

    [Fact]
    public void AnalyzeSpread_CalculatesCorrectly()
    {
        var (spread, spreadBps, midPrice) = OrderBookAnalysis.AnalyzeSpread(99m, 101m);

        Assert.Equal(2m, spread);
        Assert.Equal(100m, midPrice);
        Assert.True(spreadBps > 0);
        TestHelpers.AssertApproximately(200m, spreadBps, 1m); // ~200 bps
    }

    [Fact]
    public void CalculateDepth_SumsCorrectly()
    {
        var bids = new[] { (100m, 10m), (99m, 20m), (98m, 15m) };
        var asks = new[] { (101m, 5m), (102m, 10m), (103m, 8m) };

        var (bidDepth, askDepth, totalDepth) = OrderBookAnalysis.CalculateDepth(bids, asks, 3);

        Assert.Equal(45m, bidDepth);
        Assert.Equal(23m, askDepth);
        Assert.Equal(68m, totalDepth);
    }

    [Fact]
    public void CalculatePriceImpact_Buy_ReturnsAveragePrice()
    {
        var asks = new[] { (101m, 10m), (102m, 20m), (103m, 15m) };
        var avgPrice = OrderBookAnalysis.CalculatePriceImpact(asks, 15m, true);

        // Should fill 10 @ 101 + 5 @ 102 = (1010 + 510) / 15 = 101.333...
        TestHelpers.AssertApproximately(101.333m, avgPrice, 0.01m);
    }

    [Fact]
    public void CalculatePriceImpact_Sell_ReturnsAveragePrice()
    {
        var bids = new[] { (100m, 10m), (99m, 20m), (98m, 15m) };
        var avgPrice = OrderBookAnalysis.CalculatePriceImpact(bids, 25m, false);

        // Should fill 10 @ 100 + 15 @ 99 = (1000 + 1485) / 25 = 99.4
        TestHelpers.AssertApproximately(99.4m, avgPrice, 0.01m);
    }
}
