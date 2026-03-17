using Rhodium.Primitives;
using Rhodium.Indicators;

namespace Rhodium.Indicators.Tests;

public class ZScoreTests
{
    [Fact]
    public void ZScore_CalculatesCorrectValue()
    {
        var zscore = Indicators.ZScore(5);
        var prices = TestHelpers.Prices(10m, 12m, 11m, 13m, 12m, 15m);
        TestHelpers.UpdatePrices(zscore, prices);

        Assert.True(zscore.IsReady);
        // 15 is above mean, so z-score should be positive
        Assert.True(zscore.Value > 0);
    }

    [Fact]
    public void ZScore_MeanValue_IsNearZero()
    {
        var zscore = Indicators.ZScore(5);
        var prices = TestHelpers.ConstantPrices(100m, 10);
        TestHelpers.UpdatePrices(zscore, prices);

        TestHelpers.AssertApproximately(0m, zscore.Value);
    }

    [Fact]
    public void ZScore_BecomesReady_AfterPeriodUpdates()
    {
        var zscore = Indicators.ZScore(5);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 10);
        TestHelpers.TestReadinessAfterPeriod(zscore, 5, prices);
    }
}

public class LinearRegTests
{
    [Fact]
    public void LinearReg_CalculatesCorrectValue()
    {
        var linreg = Indicators.LinearReg(5);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 10);
        TestHelpers.UpdatePrices(linreg, prices);

        Assert.True(linreg.IsReady);
        // Linear regression should predict next value in ascending sequence
        Assert.True(linreg.Value > 100m);
    }

    [Fact]
    public void LinearReg_ConstantPrices_EqualsPrice()
    {
        var linreg = Indicators.LinearReg(5);
        var prices = TestHelpers.ConstantPrices(100m, 10);
        TestHelpers.UpdatePrices(linreg, prices);

        TestHelpers.AssertApproximately(100m, linreg.Value, TestHelpers.LowPrecision);
    }

    [Fact]
    public void LinearReg_BecomesReady_AfterPeriodUpdates()
    {
        var linreg = Indicators.LinearReg(5);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 10);
        TestHelpers.TestReadinessAfterPeriod(linreg, 5, prices);
    }
}

public class LinearRegSlopeTests
{
    [Fact]
    public void LinearRegSlope_PositiveForAscending()
    {
        var slope = Indicators.LinearRegSlope(5);
        var prices = TestHelpers.AscendingPrices(100m, 2m, 10);
        TestHelpers.UpdatePrices(slope, prices);

        Assert.True(slope.IsReady);
        Assert.True(slope.Value > 0, "Slope should be positive for ascending prices");
    }

    [Fact]
    public void LinearRegSlope_NegativeForDescending()
    {
        var slope = Indicators.LinearRegSlope(5);
        var prices = TestHelpers.DescendingPrices(100m, 2m, 10);
        TestHelpers.UpdatePrices(slope, prices);

        Assert.True(slope.IsReady);
        Assert.True(slope.Value < 0, "Slope should be negative for descending prices");
    }

    [Fact]
    public void LinearRegSlope_NearZeroForConstant()
    {
        var slope = Indicators.LinearRegSlope(5);
        var prices = TestHelpers.ConstantPrices(100m, 10);
        TestHelpers.UpdatePrices(slope, prices);

        TestHelpers.AssertApproximately(0m, slope.Value, TestHelpers.LowPrecision);
    }
}

public class MaxTests
{
    [Fact]
    public void Max_FindsMaximumValue()
    {
        var max = Indicators.Max(5);
        var prices = TestHelpers.Prices(10m, 20m, 15m, 25m, 12m, 18m);
        TestHelpers.UpdatePrices(max, prices);

        Assert.Equal(25m, max.Value);
    }

    [Fact]
    public void Max_UpdatesWithSlidingWindow()
    {
        var max = Indicators.Max(3);
        TestHelpers.UpdatePrices(max, 10m, 20m, 30m);
        Assert.Equal(30m, max.Value);

        max.Update(15m); // Window now [20, 30, 15]
        Assert.Equal(30m, max.Value);

        max.Update(10m); // Window now [30, 15, 10]
        Assert.Equal(30m, max.Value);

        max.Update(5m); // Window now [15, 10, 5]
        Assert.Equal(15m, max.Value);
    }

    [Fact]
    public void Max_BecomesReady_AfterPeriodUpdates()
    {
        var max = Indicators.Max(5);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 10);
        TestHelpers.TestReadinessAfterPeriod(max, 5, prices);
    }
}

public class MinTests
{
    [Fact]
    public void Min_FindsMinimumValue()
    {
        var min = Indicators.Min(5);
        var prices = TestHelpers.Prices(10m, 20m, 15m, 5m, 12m, 18m);
        TestHelpers.UpdatePrices(min, prices);

        Assert.Equal(5m, min.Value);
    }

    [Fact]
    public void Min_UpdatesWithSlidingWindow()
    {
        var min = Indicators.Min(3);
        TestHelpers.UpdatePrices(min, 30m, 20m, 10m);
        Assert.Equal(10m, min.Value);

        min.Update(25m); // Window now [20, 10, 25]
        Assert.Equal(10m, min.Value);

        min.Update(30m); // Window now [10, 25, 30]
        Assert.Equal(10m, min.Value);

        min.Update(35m); // Window now [25, 30, 35]
        Assert.Equal(25m, min.Value);
    }

    [Fact]
    public void Min_BecomesReady_AfterPeriodUpdates()
    {
        var min = Indicators.Min(5);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 10);
        TestHelpers.TestReadinessAfterPeriod(min, 5, prices);
    }
}

public class SumTests
{
    [Fact]
    public void Sum_CalculatesCorrectSum()
    {
        var sum = Indicators.Sum(3);
        var prices = TestHelpers.Prices(10m, 20m, 30m);
        TestHelpers.UpdatePrices(sum, prices);

        Assert.Equal(60m, sum.Value);
    }

    [Fact]
    public void Sum_UpdatesWithSlidingWindow()
    {
        var sum = Indicators.Sum(3);
        TestHelpers.UpdatePrices(sum, 10m, 20m, 30m);
        Assert.Equal(60m, sum.Value);

        sum.Update(40m); // Window now [20, 30, 40]
        Assert.Equal(90m, sum.Value);
    }

    [Fact]
    public void Sum_BecomesReady_AfterPeriodUpdates()
    {
        var sum = Indicators.Sum(5);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 10);
        TestHelpers.TestReadinessAfterPeriod(sum, 5, prices);
    }
}

public class EfficiencyRatioTests
{
    [Fact]
    public void EfficiencyRatio_PerfectTrend_EqualsOne()
    {
        var er = Indicators.EfficiencyRatio(5);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 10);
        TestHelpers.UpdatePrices(er, prices);

        Assert.True(er.IsReady);
        TestHelpers.AssertApproximately(1m, er.Value, TestHelpers.LowPrecision);
    }

    [Fact]
    public void EfficiencyRatio_Oscillating_IsLow()
    {
        var er = Indicators.EfficiencyRatio(5);
        var prices = TestHelpers.OscillatingPrices(100m, 110m, 10);
        TestHelpers.UpdatePrices(er, prices);

        Assert.True(er.IsReady);
        // Oscillating prices have low efficiency
        Assert.True(er.Value < 0.5m);
    }

    [Fact]
    public void EfficiencyRatio_ConstantPrices_IsZero()
    {
        var er = Indicators.EfficiencyRatio(5);
        var prices = TestHelpers.ConstantPrices(100m, 10);
        TestHelpers.UpdatePrices(er, prices);

        TestHelpers.AssertApproximately(0m, er.Value);
    }
}
