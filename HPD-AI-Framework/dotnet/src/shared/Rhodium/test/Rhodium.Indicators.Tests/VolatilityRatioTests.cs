using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

public class VolatilityRatioTests
{
    [Fact]
    public void BasicFunctionality_CalculatesRatioOfCurrentTRtoATR()
    {
        var vr = Indicators.VolatilityRatio(14);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 20));

        TestHelpers.UpdateBars(vr, bars);

        TestHelpers.AssertReady(vr);
        Assert.True(vr.Value > 0);
    }

    [Fact]
    public void BecomesReadyAfterPeriodPlusOne()
    {
        var vr = Indicators.VolatilityRatio(14);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 20));

        // Should not be ready for first bar (count = 1)
        vr.Update(bars[0]);
        TestHelpers.AssertNotReady(vr);

        // Update until ATR is ready (period bars after first)
        for (int i = 1; i < 14; i++)
        {
            vr.Update(bars[i]);
            TestHelpers.AssertNotReady(vr);
        }

        // Should be ready after period bars (count > 1 and ATR ready)
        vr.Update(bars[14]);
        TestHelpers.AssertReady(vr);
    }

    [Fact]
    public void ResetClearsState()
    {
        var vr = Indicators.VolatilityRatio(10);

        TestHelpers.TestReset(vr, () =>
        {
            var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 15));
            TestHelpers.UpdateBars(vr, bars);
        });
    }

    [Fact]
    public void RatioOfOneWhenCurrentTREqualsATR()
    {
        var vr = Indicators.VolatilityRatio(5);

        // Create bars with consistent true range
        var bars = new[]
        {
            TestHelpers.CreateBar(100m, 110m, 95m, 105m), // TR = 15
            TestHelpers.CreateBar(105m, 120m, 100m, 115m), // TR = 20
            TestHelpers.CreateBar(115m, 130m, 110m, 125m), // TR = 20
            TestHelpers.CreateBar(125m, 140m, 120m, 135m), // TR = 20
            TestHelpers.CreateBar(135m, 150m, 130m, 145m), // TR = 20
            TestHelpers.CreateBar(145m, 160m, 140m, 155m)  // TR = 20
        };

        TestHelpers.UpdateBars(vr, bars);

        TestHelpers.AssertReady(vr);
        // After stabilizing with consistent TR, ratio should be close to 1
        TestHelpers.AssertApproximately(1m, vr.Value, 0.5m);
    }

    [Fact]
    public void HighRatioWhenCurrentVolatilitySpikes()
    {
        var vr = Indicators.VolatilityRatio(10);

        // Low volatility bars
        var bars = new List<Bar>();
        for (int i = 0; i < 15; i++)
        {
            bars.Add(TestHelpers.CreateBar(100m, 102m, 99m, 100m)); // TR = 3
        }

        TestHelpers.UpdateBars(vr, bars.ToArray());

        var normalRatio = vr.Value;

        // Add high volatility bar
        var spikeBar = TestHelpers.CreateBar(100m, 130m, 85m, 110m); // TR = 45
        vr.Update(spikeBar);

        // Ratio should spike
        Assert.True(vr.Value > normalRatio);
        Assert.True(vr.Value > 5m); // Should be much higher than ATR
    }

    [Fact]
    public void LowRatioWhenCurrentVolatilityDrops()
    {
        var vr = Indicators.VolatilityRatio(10);

        // High volatility bars
        var bars = new List<Bar>();
        for (int i = 0; i < 15; i++)
        {
            bars.Add(TestHelpers.CreateBar(100m, 120m, 80m, 110m)); // TR = 40
        }

        TestHelpers.UpdateBars(vr, bars.ToArray());

        // Add low volatility bar
        var quietBar = TestHelpers.CreateBar(110m, 111m, 109m, 110m); // TR = 2
        vr.Update(quietBar);

        // Ratio should be low
        Assert.True(vr.Value < 1m);
        Assert.True(vr.Value > 0m);
    }

    [Fact]
    public void HandlesZeroATRGracefully()
    {
        var vr = Indicators.VolatilityRatio(5);

        // Create bars with no range (constant OHLC)
        var bars = TestHelpers.CreateBars(TestHelpers.ConstantPrices(100m, 10));

        TestHelpers.UpdateBars(vr, bars);

        // Should default to 1 when ATR is 0
        TestHelpers.AssertReady(vr);
        TestHelpers.AssertApproximately(1m, vr.Value, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void VolatilityIncreasesRatio()
    {
        var vr1 = Indicators.VolatilityRatio(10);
        var vr2 = Indicators.VolatilityRatio(10);

        // Low volatility baseline
        var lowVolBars = new List<Bar>();
        for (int i = 0; i < 15; i++)
        {
            lowVolBars.Add(TestHelpers.CreateBar(100m, 102m, 99m, 100m));
        }

        TestHelpers.UpdateBars(vr1, lowVolBars.ToArray());

        // High volatility current
        var mixedBars = new List<Bar>(lowVolBars);
        mixedBars.Add(TestHelpers.CreateBar(100m, 115m, 90m, 105m)); // High TR spike

        TestHelpers.UpdateBars(vr2, mixedBars.ToArray());

        // vr2 should have higher ratio due to the spike
        Assert.True(vr2.Value > vr1.Value);
    }

    [Fact]
    public void DifferentPeriods()
    {
        var vr5 = Indicators.VolatilityRatio(5);
        var vr20 = Indicators.VolatilityRatio(20);

        var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 30));

        TestHelpers.UpdateBars(vr5, bars);
        TestHelpers.UpdateBars(vr20, bars);

        TestHelpers.AssertReady(vr5);
        TestHelpers.AssertReady(vr20);

        Assert.True(vr5.Value > 0);
        Assert.True(vr20.Value > 0);
    }

    [Fact]
    public void AlwaysPositive()
    {
        var vr = Indicators.VolatilityRatio(10);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.OscillatingPrices(90m, 110m, 20));

        TestHelpers.UpdateBars(vr, bars);

        Assert.True(vr.Value >= 0);
    }
}
