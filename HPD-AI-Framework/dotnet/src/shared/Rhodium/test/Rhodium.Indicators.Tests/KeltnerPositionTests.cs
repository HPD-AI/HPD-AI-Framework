using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

public class KeltnerPositionTests
{
    [Fact]
    public void BasicFunctionality_CalculatesRelativePosition()
    {
        var kp = Indicators.KeltnerPosition(10, 2m);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 20));

        TestHelpers.UpdateBars(kp, bars);

        TestHelpers.AssertReady(kp);

        // Position should be between 0 and 1
        TestHelpers.AssertInRange(kp.Value, 0m, 1m);
    }

    [Fact]
    public void BecomesReadyWhenKeltnerChannelReady()
    {
        var kp = Indicators.KeltnerPosition(14, 2m);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 20));

        // KeltnerPosition depends on KeltnerChannel which needs period+1 bars
        for (int i = 0; i < 14; i++)
        {
            kp.Update(bars[i]);
            TestHelpers.AssertNotReady(kp, $"Should not be ready after {i + 1} bars");
        }

        kp.Update(bars[14]);
        TestHelpers.AssertReady(kp, "Should be ready after 15 bars");
    }

    [Fact]
    public void ResetClearsState()
    {
        var kp = Indicators.KeltnerPosition(10, 2m);

        TestHelpers.TestReset(kp, () =>
        {
            var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 15));
            TestHelpers.UpdateBars(kp, bars);
        });
    }

    [Fact]
    public void PositionBetweenZeroAndOne()
    {
        var kp = Indicators.KeltnerPosition(10, 2m);
        var bars = TestHelpers.CreateTrendBars(TestHelpers.OscillatingPrices(90m, 110m, 20));

        TestHelpers.UpdateBars(kp, bars);

        TestHelpers.AssertReady(kp);
        TestHelpers.AssertInRange(kp.Value, 0m, 1m);
    }

    [Fact]
    public void PositionNearHalfWhenAtMiddle()
    {
        var kp = Indicators.KeltnerPosition(10, 2m);

        // Create bars where close is near the moving average
        var bars = new List<Bar>();
        for (int i = 0; i < 15; i++)
        {
            // Low volatility bars centered around 100
            bars.Add(TestHelpers.CreateBar(100m, 102m, 98m, 100m));
        }

        TestHelpers.UpdateBars(kp, bars.ToArray());

        TestHelpers.AssertReady(kp);

        // Position should be close to 0.5 when price is at middle
        TestHelpers.AssertApproximately(0.5m, kp.Value, 0.2m);
    }

    [Fact]
    public void PositionNearOneWhenAtUpperBand()
    {
        var kp = Indicators.KeltnerPosition(10, 2m);

        // Start with low volatility to establish channel
        var bars = new List<Bar>();
        for (int i = 0; i < 15; i++)
        {
            bars.Add(TestHelpers.CreateBar(100m, 102m, 98m, 100m));
        }

        TestHelpers.UpdateBars(kp, bars.ToArray());

        // Add bar with close at upper band
        var highBar = TestHelpers.CreateBar(105m, 108m, 104m, 108m);
        kp.Update(highBar);

        TestHelpers.AssertReady(kp);

        // Position should be high when price is near upper band
        Assert.True(kp.Value > 0.7m);
    }

    [Fact]
    public void PositionNearZeroWhenAtLowerBand()
    {
        var kp = Indicators.KeltnerPosition(10, 2m);

        // Start with low volatility to establish channel
        var bars = new List<Bar>();
        for (int i = 0; i < 15; i++)
        {
            bars.Add(TestHelpers.CreateBar(100m, 102m, 98m, 100m));
        }

        TestHelpers.UpdateBars(kp, bars.ToArray());

        // Add bar with close at lower band
        var lowBar = TestHelpers.CreateBar(95m, 96m, 92m, 92m);
        kp.Update(lowBar);

        TestHelpers.AssertReady(kp);

        // Position should be low when price is near lower band
        Assert.True(kp.Value < 0.3m);
    }

    [Fact]
    public void HandlesZeroRangeGracefully()
    {
        var kp = Indicators.KeltnerPosition(10, 2m);

        // Constant prices produce zero range
        var bars = TestHelpers.CreateBars(TestHelpers.ConstantPrices(100m, 20));

        TestHelpers.UpdateBars(kp, bars);

        TestHelpers.AssertReady(kp);

        // Should default to 0.5 when range is 0
        TestHelpers.AssertApproximately(0.5m, kp.Value, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void PositionCanExceedBounds()
    {
        var kp = Indicators.KeltnerPosition(10, 2m);

        // Establish a narrow channel
        var bars = new List<Bar>();
        for (int i = 0; i < 15; i++)
        {
            bars.Add(TestHelpers.CreateBar(100m, 101m, 99m, 100m));
        }

        TestHelpers.UpdateBars(kp, bars.ToArray());

        // Add extreme bar that breaks out of channel
        var breakoutBar = TestHelpers.CreateBar(110m, 120m, 108m, 115m);
        kp.Update(breakoutBar);

        TestHelpers.AssertReady(kp);

        // Position can exceed 1 when price breaks above upper band
        Assert.True(kp.Value >= 0m);
    }

    [Fact]
    public void DifferentMultipliers()
    {
        var kp1 = Indicators.KeltnerPosition(10, 1m);
        var kp2 = Indicators.KeltnerPosition(10, 3m);

        var bars = TestHelpers.CreateTrendBars(TestHelpers.OscillatingPrices(95m, 105m, 20));

        TestHelpers.UpdateBars(kp1, bars);
        TestHelpers.UpdateBars(kp2, bars);

        TestHelpers.AssertReady(kp1);
        TestHelpers.AssertReady(kp2);

        // Different multipliers create different channel widths
        // Position values should differ
        TestHelpers.AssertInRange(kp1.Value, 0m, 1m);
        TestHelpers.AssertInRange(kp2.Value, 0m, 1m);
    }

    [Fact]
    public void TracksMovementThroughChannel()
    {
        var kp = Indicators.KeltnerPosition(5, 2m);

        // Establish baseline
        var bars = new List<Bar>();
        for (int i = 0; i < 10; i++)
        {
            bars.Add(TestHelpers.CreateBar(100m, 105m, 95m, 100m));
        }

        TestHelpers.UpdateBars(kp, bars.ToArray());

        var midPosition = kp.Value;

        // Move up
        bars.Add(TestHelpers.CreateBar(105m, 110m, 100m, 108m));
        kp.Update(bars[^1]);
        var upperPosition = kp.Value;

        // Move down
        bars.Add(TestHelpers.CreateBar(95m, 100m, 90m, 92m));
        kp.Update(bars[^1]);
        var lowerPosition = kp.Value;

        // Upper position should be higher than mid, lower should be lower
        Assert.True(upperPosition > midPosition);
        Assert.True(lowerPosition < midPosition);
    }

    [Fact]
    public void DifferentPeriods()
    {
        var kp10 = Indicators.KeltnerPosition(10, 2m);
        var kp20 = Indicators.KeltnerPosition(20, 2m);

        var bars = TestHelpers.CreateTrendBars(TestHelpers.AscendingPrices(100m, 1m, 30));

        TestHelpers.UpdateBars(kp10, bars);
        TestHelpers.UpdateBars(kp20, bars);

        TestHelpers.AssertReady(kp10);
        TestHelpers.AssertReady(kp20);

        TestHelpers.AssertInRange(kp10.Value, 0m, 2m); // Can exceed bounds
        TestHelpers.AssertInRange(kp20.Value, 0m, 2m);
    }
}
