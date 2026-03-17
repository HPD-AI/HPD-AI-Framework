using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

public class ATRTests
{
    [Fact]
    public void BasicFunctionality_CalculatesAverageTrueRange()
    {
        var atr = Indicators.ATR(14);

        // Create bars with known true ranges
        var bars = new[]
        {
            TestHelpers.CreateBar(100m, 105m, 95m, 102m),
            TestHelpers.CreateBar(102m, 108m, 100m, 106m),
            TestHelpers.CreateBar(106m, 110m, 104m, 108m),
            TestHelpers.CreateBar(108m, 112m, 106m, 110m),
            TestHelpers.CreateBar(110m, 115m, 108m, 112m),
            TestHelpers.CreateBar(112m, 118m, 110m, 115m),
            TestHelpers.CreateBar(115m, 120m, 113m, 118m),
            TestHelpers.CreateBar(118m, 122m, 116m, 120m),
            TestHelpers.CreateBar(120m, 125m, 118m, 122m),
            TestHelpers.CreateBar(122m, 126m, 120m, 124m),
            TestHelpers.CreateBar(124m, 128m, 122m, 126m),
            TestHelpers.CreateBar(126m, 130m, 124m, 128m),
            TestHelpers.CreateBar(128m, 132m, 126m, 130m),
            TestHelpers.CreateBar(130m, 134m, 128m, 132m),
            TestHelpers.CreateBar(132m, 136m, 130m, 134m)
        };

        TestHelpers.UpdateBars(atr, bars);

        TestHelpers.AssertReady(atr);
        Assert.True(atr.Value > 0);
    }

    [Fact]
    public void BecomesReadyAfterPeriod()
    {
        var atr = Indicators.ATR(14);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 20));

        // ATR skips first bar, then needs RMA to be ready (period bars)
        // So total bars needed = 1 (skipped) + 14 (RMA period) = 15 bars
        for (int i = 0; i < 14; i++)
        {
            atr.Update(bars[i]);
            TestHelpers.AssertNotReady(atr, $"Should not be ready after {i + 1} bars");
        }

        atr.Update(bars[14]);
        TestHelpers.AssertReady(atr, "Should be ready after period+1 bars");
    }

    [Fact]
    public void ResetClearsState()
    {
        var atr = Indicators.ATR(10);

        TestHelpers.TestReset(atr, () =>
        {
            var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 15));
            TestHelpers.UpdateBars(atr, bars);
        });
    }

    [Fact]
    public void HigherVolatilityIncreasesATR()
    {
        var atr1 = Indicators.ATR(10);
        var atr2 = Indicators.ATR(10);

        // Low volatility bars
        var lowVolBars = new[]
        {
            TestHelpers.CreateBar(100m, 100.5m, 99.5m, 100m),
            TestHelpers.CreateBar(100m, 100.5m, 99.5m, 100m),
            TestHelpers.CreateBar(100m, 100.5m, 99.5m, 100m),
            TestHelpers.CreateBar(100m, 100.5m, 99.5m, 100m),
            TestHelpers.CreateBar(100m, 100.5m, 99.5m, 100m),
            TestHelpers.CreateBar(100m, 100.5m, 99.5m, 100m),
            TestHelpers.CreateBar(100m, 100.5m, 99.5m, 100m),
            TestHelpers.CreateBar(100m, 100.5m, 99.5m, 100m),
            TestHelpers.CreateBar(100m, 100.5m, 99.5m, 100m),
            TestHelpers.CreateBar(100m, 100.5m, 99.5m, 100m),
            TestHelpers.CreateBar(100m, 100.5m, 99.5m, 100m)
        };

        // High volatility bars
        var highVolBars = new[]
        {
            TestHelpers.CreateBar(100m, 110m, 90m, 105m),
            TestHelpers.CreateBar(105m, 115m, 95m, 100m),
            TestHelpers.CreateBar(100m, 110m, 90m, 108m),
            TestHelpers.CreateBar(108m, 118m, 98m, 102m),
            TestHelpers.CreateBar(102m, 112m, 92m, 110m),
            TestHelpers.CreateBar(110m, 120m, 100m, 105m),
            TestHelpers.CreateBar(105m, 115m, 95m, 112m),
            TestHelpers.CreateBar(112m, 122m, 102m, 108m),
            TestHelpers.CreateBar(108m, 118m, 98m, 115m),
            TestHelpers.CreateBar(115m, 125m, 105m, 110m),
            TestHelpers.CreateBar(110m, 120m, 100m, 118m)
        };

        TestHelpers.UpdateBars(atr1, lowVolBars);
        TestHelpers.UpdateBars(atr2, highVolBars);

        Assert.True(atr2.Value > atr1.Value);
    }

    [Fact]
    public void TrueRangeAccountsForGaps()
    {
        var atr = Indicators.ATR(3);

        // Bar with gap down - TR should be high - low of current bar initially
        var bar1 = TestHelpers.CreateBar(100m, 105m, 95m, 100m);
        atr.Update(bar1);

        // Gap down - previous close is 100, current low is 80
        // TR = max(high-low, |high-prevClose|, |low-prevClose|)
        // TR = max(10, 10, 20) = 20
        var bar2 = TestHelpers.CreateBar(85m, 90m, 80m, 85m);
        atr.Update(bar2);

        // Gap up - previous close is 85, current high is 110
        // TR = max(10, 25, 15) = 25
        var bar3 = TestHelpers.CreateBar(100m, 110m, 100m, 105m);
        atr.Update(bar3);

        var bar4 = TestHelpers.CreateBar(105m, 112m, 103m, 108m);
        atr.Update(bar4);

        TestHelpers.AssertReady(atr);
        // ATR should be relatively high due to gaps
        Assert.True(atr.Value > 10m);
    }

    [Fact]
    public void FirstBarInitializesCorrectly()
    {
        var atr = Indicators.ATR(14);
        var bar = TestHelpers.CreateBar(100m, 105m, 95m, 102m);

        atr.Update(bar);

        Assert.Equal(1, atr.Count);
        TestHelpers.AssertNotReady(atr);
    }

    [Fact]
    public void AlwaysPositive()
    {
        var atr = Indicators.ATR(10);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.DescendingPrices(200m, 2m, 15));

        TestHelpers.UpdateBars(atr, bars);

        Assert.True(atr.Value >= 0);
    }

    [Fact]
    public void DifferentPeriods()
    {
        var atr5 = Indicators.ATR(5);
        var atr20 = Indicators.ATR(20);

        var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 30));

        TestHelpers.UpdateBars(atr5, bars);
        TestHelpers.UpdateBars(atr20, bars);

        // Both should be ready
        TestHelpers.AssertReady(atr5);
        TestHelpers.AssertReady(atr20);

        // Values may differ due to different averaging periods
        Assert.True(atr5.Value > 0);
        Assert.True(atr20.Value > 0);
    }

    [Fact]
    public void ConstantBarsProduceLowATR()
    {
        var atr = Indicators.ATR(10);
        var bars = TestHelpers.CreateBars(TestHelpers.ConstantPrices(100m, 15));

        TestHelpers.UpdateBars(atr, bars);

        // With constant prices (no range), ATR should be 0
        TestHelpers.AssertApproximately(0m, atr.Value, TestHelpers.DefaultPrecision);
    }
}
