using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

public class BollingerBandsTests
{
    [Fact]
    public void BasicFunctionality_CalculatesCorrectBands()
    {
        var bb = Indicators.BollingerBands(5, 2m);
        var prices = TestHelpers.Prices(100m, 102m, 104m, 103m, 101m, 105m);

        TestHelpers.UpdatePrices(bb, prices);

        TestHelpers.AssertReady(bb);

        // Calculate expected values manually
        var lastFive = new[] { 102m, 104m, 103m, 101m, 105m };
        var expectedMiddle = TestHelpers.CalculateSMA(lastFive);
        var expectedStdDev = TestHelpers.CalculateStdDev(lastFive);
        var expectedUpper = expectedMiddle + (2m * expectedStdDev);
        var expectedLower = expectedMiddle - (2m * expectedStdDev);

        TestHelpers.AssertApproximately(expectedMiddle, bb.Middle, TestHelpers.DefaultPrecision);
        TestHelpers.AssertApproximately(expectedMiddle, bb.Value, TestHelpers.DefaultPrecision);
        TestHelpers.AssertApproximately(expectedUpper, bb.Upper, TestHelpers.DefaultPrecision);
        TestHelpers.AssertApproximately(expectedLower, bb.Lower, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void BecomesReadyAfterPeriod()
    {
        var bb = Indicators.BollingerBands(20, 2m);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 25);

        TestHelpers.TestReadinessAfterPeriod(bb, 20, prices);
    }

    [Fact]
    public void ResetClearsState()
    {
        var bb = Indicators.BollingerBands(10, 2m);

        TestHelpers.TestReset(bb, () =>
        {
            var prices = TestHelpers.AscendingPrices(100m, 1m, 15);
            TestHelpers.UpdatePrices(bb, prices);
        });

        Assert.Equal(0m, bb.Upper);
        Assert.Equal(0m, bb.Middle);
        Assert.Equal(0m, bb.Lower);
    }

    [Fact]
    public void UpperBandAboveLowerBand()
    {
        var bb = Indicators.BollingerBands(10, 2m);
        var prices = TestHelpers.OscillatingPrices(95m, 105m, 20);

        TestHelpers.UpdatePrices(bb, prices);

        Assert.True(bb.Upper >= bb.Middle);
        Assert.True(bb.Middle >= bb.Lower);
    }

    [Fact]
    public void BandsWideningWithHighVolatility()
    {
        var bb = Indicators.BollingerBands(10, 2m);

        // Low volatility (constant prices)
        var constantPrices = TestHelpers.ConstantPrices(100m, 15);
        TestHelpers.UpdatePrices(bb, constantPrices);
        var lowVolatilityWidth = bb.Upper - bb.Lower;

        // Reset and test high volatility
        bb.Reset();
        var oscillatingPrices = TestHelpers.OscillatingPrices(90m, 110m, 15);
        TestHelpers.UpdatePrices(bb, oscillatingPrices);
        var highVolatilityWidth = bb.Upper - bb.Lower;

        Assert.True(highVolatilityWidth > lowVolatilityWidth);
    }

    [Fact]
    public void BandsNarrowWithConstantPrices()
    {
        var bb = Indicators.BollingerBands(10, 2m);
        var prices = TestHelpers.ConstantPrices(100m, 15);

        TestHelpers.UpdatePrices(bb, prices);

        // With constant prices, std dev should be 0, so bands should converge
        TestHelpers.AssertApproximately(100m, bb.Middle, TestHelpers.DefaultPrecision);
        TestHelpers.AssertApproximately(0m, bb.Upper - bb.Lower, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void DifferentMultipliers()
    {
        var bb1 = Indicators.BollingerBands(10, 1m);
        var bb2 = Indicators.BollingerBands(10, 2m);
        var bb3 = Indicators.BollingerBands(10, 3m);

        var prices = TestHelpers.OscillatingPrices(95m, 105m, 15);

        TestHelpers.UpdatePrices(bb1, prices);
        bb2.Reset();
        TestHelpers.UpdatePrices(bb2, prices);
        bb3.Reset();
        TestHelpers.UpdatePrices(bb3, prices);

        // Middle should be the same
        TestHelpers.AssertApproximately(bb1.Middle, bb2.Middle, TestHelpers.DefaultPrecision);
        TestHelpers.AssertApproximately(bb2.Middle, bb3.Middle, TestHelpers.DefaultPrecision);

        // Width should increase with multiplier
        var width1 = bb1.Upper - bb1.Lower;
        var width2 = bb2.Upper - bb2.Lower;
        var width3 = bb3.Upper - bb3.Lower;

        Assert.True(width1 < width2);
        Assert.True(width2 < width3);
    }

    [Fact]
    public void HandleZeroPrices()
    {
        var bb = Indicators.BollingerBands(10, 2m);
        TestHelpers.TestZeroPrices(bb, 15);
    }

    [Fact]
    public void HandleLargePrices()
    {
        var bb = Indicators.BollingerBands(10, 2m);
        TestHelpers.TestLargePrices(bb);
    }

    [Fact]
    public void ValueEqualsMiddleBand()
    {
        var bb = Indicators.BollingerBands(10, 2m);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 15);

        TestHelpers.UpdatePrices(bb, prices);

        Assert.Equal(bb.Middle, bb.Value);
    }
}
