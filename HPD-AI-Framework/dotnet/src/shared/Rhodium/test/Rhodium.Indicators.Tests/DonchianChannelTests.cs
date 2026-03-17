using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

public class DonchianChannelTests
{
    [Fact]
    public void BasicFunctionality_TracksHighestHighAndLowestLow()
    {
        var dc = Indicators.DonchianChannel(5);

        var bars = new[]
        {
            TestHelpers.CreateBar(100m, 105m, 95m, 102m),
            TestHelpers.CreateBar(102m, 110m, 98m, 108m),
            TestHelpers.CreateBar(108m, 115m, 105m, 112m),
            TestHelpers.CreateBar(112m, 112m, 106m, 108m),
            TestHelpers.CreateBar(108m, 113m, 104m, 110m)
        };

        TestHelpers.UpdateBars(dc, bars);

        TestHelpers.AssertReady(dc);

        // Upper should be highest high: 115
        // Lower should be lowest low: 95
        TestHelpers.AssertApproximately(115m, dc.Upper, TestHelpers.DefaultPrecision);
        TestHelpers.AssertApproximately(95m, dc.Lower, TestHelpers.DefaultPrecision);
        TestHelpers.AssertApproximately((115m + 95m) / 2m, dc.Middle, TestHelpers.DefaultPrecision);
        TestHelpers.AssertApproximately(dc.Middle, dc.Value, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void BecomesReadyAfterPeriod()
    {
        var dc = Indicators.DonchianChannel(20);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 25));

        for (int i = 0; i < 19; i++)
        {
            dc.Update(bars[i]);
            TestHelpers.AssertNotReady(dc);
        }

        dc.Update(bars[19]);
        TestHelpers.AssertReady(dc);
    }

    [Fact]
    public void ResetClearsState()
    {
        var dc = Indicators.DonchianChannel(10);

        TestHelpers.TestReset(dc, () =>
        {
            var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 15));
            TestHelpers.UpdateBars(dc, bars);
        });

        Assert.Equal(0m, dc.Upper);
        Assert.Equal(0m, dc.Middle);
        Assert.Equal(0m, dc.Lower);
    }

    [Fact]
    public void UpperAboveOrEqualLower()
    {
        var dc = Indicators.DonchianChannel(10);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.OscillatingPrices(90m, 110m, 20));

        TestHelpers.UpdateBars(dc, bars);

        TestHelpers.AssertReady(dc);
        Assert.True(dc.Upper >= dc.Lower);
    }

    [Fact]
    public void MiddleIsMidpointOfUpperAndLower()
    {
        var dc = Indicators.DonchianChannel(10);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 15));

        TestHelpers.UpdateBars(dc, bars);

        TestHelpers.AssertReady(dc);
        var expectedMiddle = (dc.Upper + dc.Lower) / 2m;
        TestHelpers.AssertApproximately(expectedMiddle, dc.Middle, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void ValueEqualsMiddle()
    {
        var dc = Indicators.DonchianChannel(10);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.OscillatingPrices(95m, 105m, 15));

        TestHelpers.UpdateBars(dc, bars);

        TestHelpers.AssertReady(dc);
        Assert.Equal(dc.Middle, dc.Value);
    }

    [Fact]
    public void ChannelWidensWithVolatility()
    {
        var dc1 = Indicators.DonchianChannel(10);
        var dc2 = Indicators.DonchianChannel(10);

        // Low volatility
        var lowVolBars = TestHelpers.CreateTrendBars(TestHelpers.ConstantPrices(100m, 15));
        TestHelpers.UpdateBars(dc1, lowVolBars);

        var lowVolWidth = dc1.Upper - dc1.Lower;

        // High volatility
        var highVolBars = TestHelpers.CreateTrendBars(TestHelpers.OscillatingPrices(80m, 120m, 15));
        TestHelpers.UpdateBars(dc2, highVolBars);

        var highVolWidth = dc2.Upper - dc2.Lower;

        Assert.True(highVolWidth > lowVolWidth);
    }

    [Fact]
    public void ConstantPricesProduceZeroWidth()
    {
        var dc = Indicators.DonchianChannel(10);
        var bars = TestHelpers.CreateBars(TestHelpers.ConstantPrices(100m, 15));

        TestHelpers.UpdateBars(dc, bars);

        TestHelpers.AssertReady(dc);

        // All bars have same high and low
        TestHelpers.AssertApproximately(100m, dc.Upper, TestHelpers.DefaultPrecision);
        TestHelpers.AssertApproximately(100m, dc.Lower, TestHelpers.DefaultPrecision);
        TestHelpers.AssertApproximately(0m, dc.Upper - dc.Lower, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void TracksRollingWindow()
    {
        var dc = Indicators.DonchianChannel(3);

        var bars = new[]
        {
            TestHelpers.CreateBar(100m, 110m, 90m, 100m),   // High: 110, Low: 90
            TestHelpers.CreateBar(100m, 105m, 95m, 100m),   // High: 105, Low: 95
            TestHelpers.CreateBar(100m, 115m, 85m, 100m),   // High: 115, Low: 85 (widest)
        };

        TestHelpers.UpdateBars(dc, bars);

        // Window: [110,90], [105,95], [115,85] -> Upper=115, Lower=85
        TestHelpers.AssertApproximately(115m, dc.Upper, TestHelpers.DefaultPrecision);
        TestHelpers.AssertApproximately(85m, dc.Lower, TestHelpers.DefaultPrecision);

        // Add another bar, first bar should fall out of window
        var bar4 = TestHelpers.CreateBar(100m, 108m, 92m, 100m);
        dc.Update(bar4);

        // Window: [105,95], [115,85], [108,92] -> Upper=115, Lower=85
        TestHelpers.AssertApproximately(115m, dc.Upper, TestHelpers.DefaultPrecision);
        TestHelpers.AssertApproximately(85m, dc.Lower, TestHelpers.DefaultPrecision);

        // Add bar that changes the range
        var bar5 = TestHelpers.CreateBar(100m, 112m, 88m, 100m);
        dc.Update(bar5);

        // Window: [115,85], [108,92], [112,88] -> Upper=115, Lower=85
        TestHelpers.AssertApproximately(115m, dc.Upper, TestHelpers.DefaultPrecision);
        TestHelpers.AssertApproximately(85m, dc.Lower, TestHelpers.DefaultPrecision);

        // Add bar that removes the extreme high
        var bar6 = TestHelpers.CreateBar(100m, 110m, 86m, 100m);
        dc.Update(bar6);

        // Window: [108,92], [112,88], [110,86] -> Upper=112, Lower=86
        TestHelpers.AssertApproximately(112m, dc.Upper, TestHelpers.DefaultPrecision);
        TestHelpers.AssertApproximately(86m, dc.Lower, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void DifferentPeriods()
    {
        var dc10 = Indicators.DonchianChannel(10);
        var dc20 = Indicators.DonchianChannel(20);

        var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 30));

        TestHelpers.UpdateBars(dc10, bars);
        TestHelpers.UpdateBars(dc20, bars);

        TestHelpers.AssertReady(dc10);
        TestHelpers.AssertReady(dc20);

        // Longer period should capture wider range in trending market
        Assert.True(dc20.Upper >= dc10.Upper || dc20.Lower <= dc10.Lower);
    }

    [Fact]
    public void SinglePeriodChannel()
    {
        var dc = Indicators.DonchianChannel(1);
        var bar = TestHelpers.CreateBar(100m, 110m, 90m, 105m);

        dc.Update(bar);

        TestHelpers.AssertReady(dc);
        TestHelpers.AssertApproximately(110m, dc.Upper, TestHelpers.DefaultPrecision);
        TestHelpers.AssertApproximately(90m, dc.Lower, TestHelpers.DefaultPrecision);
    }
}
