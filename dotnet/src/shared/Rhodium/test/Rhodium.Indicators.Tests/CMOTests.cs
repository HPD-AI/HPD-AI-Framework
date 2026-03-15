using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;
using static Rhodium.Indicators.Tests.TestHelpers;

namespace Rhodium.Indicators.Tests;

public class CMOTests
{
    [Fact]
    public void CMO_InitialState_NotReady()
    {
        var cmo = Indicators.CMO(14);

        AssertNotReady(cmo);
        AssertCount(0, cmo);
    }

    [Fact]
    public void CMO_BecomesReady_AfterPeriodPlusOne()
    {
        var cmo = Indicators.CMO(14);
        var prices = AscendingPrices(100m, 1m, 20);

        // CMO needs period+1 prices (first price establishes baseline, then period changes)
        // After 14 updates, count=14, which is NOT > period (14)
        for (int i = 0; i < 14; i++)
        {
            cmo.Update(prices[i]);
            AssertNotReady(cmo, $"Should not be ready after {i + 1} updates");
        }

        // After 15th update, count=15, which IS > period (14)
        cmo.Update(prices[14]);
        AssertReady(cmo, "Should be ready after period+1 updates");
    }

    [Fact]
    public void CMO_Reset_ClearsState()
    {
        var cmo = Indicators.CMO(14);
        var prices = AscendingPrices(100m, 1m, 20);

        UpdatePrices(cmo, prices);
        AssertReady(cmo);

        cmo.Reset();

        AssertNotReady(cmo);
        AssertCount(0, cmo);
    }

    [Fact]
    public void CMO_IsBounded_BetweenMinusAndPlus100()
    {
        var cmo = Indicators.CMO(14);

        // Test extreme ascending
        var extremeUp = AscendingPrices(50m, 10m, 30);
        UpdatePrices(cmo, extremeUp);
        AssertInRange(cmo.Value, -100m, 100m);

        // Reset and test extreme descending
        cmo.Reset();
        var extremeDown = DescendingPrices(500m, 20m, 30);
        UpdatePrices(cmo, extremeDown);
        AssertInRange(cmo.Value, -100m, 100m);
    }

    [Fact]
    public void CMO_AscendingPrices_PositiveValue()
    {
        var cmo = Indicators.CMO(14);
        var prices = AscendingPrices(100m, 2m, 30);

        UpdatePrices(cmo, prices);
        AssertReady(cmo);

        // CMO should be positive for uptrend (more ups than downs)
        Assert.True(cmo.Value > 0m, $"CMO should be positive for ascending prices, got {cmo.Value}");
        AssertInRange(cmo.Value, -100m, 100m);
    }

    [Fact]
    public void CMO_DescendingPrices_NegativeValue()
    {
        var cmo = Indicators.CMO(14);
        var prices = DescendingPrices(150m, 2m, 30);

        UpdatePrices(cmo, prices);
        AssertReady(cmo);

        // CMO should be negative for downtrend (more downs than ups)
        Assert.True(cmo.Value < 0m, $"CMO should be negative for descending prices, got {cmo.Value}");
        AssertInRange(cmo.Value, -100m, 100m);
    }

    [Fact]
    public void CMO_ConstantPrices_ZeroValue()
    {
        var cmo = Indicators.CMO(14);
        var prices = ConstantPrices(100m, 20);

        UpdatePrices(cmo, prices);
        AssertReady(cmo);

        // CMO should be 0 when no ups or downs
        AssertApproximately(0m, cmo.Value, HighPrecision);
    }

    [Fact]
    public void CMO_PureUpMoves_ApproachesPlus100()
    {
        var cmo = Indicators.CMO(10);

        // All up moves
        var prices = AscendingPrices(100m, 5m, 20);

        UpdatePrices(cmo, prices);
        AssertReady(cmo);

        // CMO = 100 * (sumUp - sumDown) / (sumUp + sumDown) = 100 when all ups
        AssertApproximately(100m, cmo.Value, LowPrecision);
    }

    [Fact]
    public void CMO_PureDownMoves_ApproachesMinus100()
    {
        var cmo = Indicators.CMO(10);

        // All down moves
        var prices = DescendingPrices(200m, 5m, 20);

        UpdatePrices(cmo, prices);
        AssertReady(cmo);

        // CMO = 100 * (sumUp - sumDown) / (sumUp + sumDown) = -100 when all downs
        AssertApproximately(-100m, cmo.Value, LowPrecision);
    }

    [Fact]
    public void CMO_BalancedUpDown_NearZero()
    {
        var cmo = Indicators.CMO(14);

        // Alternating up and down
        var prices = new decimal[30];
        decimal price = 100m;
        for (int i = 0; i < 30; i++)
        {
            price += (i % 2 == 0) ? 1m : -1m;
            prices[i] = price;
        }

        UpdatePrices(cmo, prices);
        AssertReady(cmo);

        // CMO should be near 0 for balanced moves
        AssertInRange(cmo.Value, -30m, 30m);
    }

    [Fact]
    public void CMO_OscillatingPrices_OscillatesAroundZero()
    {
        var cmo = Indicators.CMO(14);
        var prices = OscillatingPrices(90m, 110m, 40);

        UpdatePrices(cmo, prices);
        AssertReady(cmo);

        // CMO should oscillate for oscillating prices
        AssertInRange(cmo.Value, -100m, 100m);
    }

    [Fact]
    public void CMO_RespondsToTrendChanges()
    {
        var cmo = Indicators.CMO(14);

        // Start with uptrend
        var upPrices = AscendingPrices(100m, 1m, 20);
        UpdatePrices(cmo, upPrices);
        var cmoAfterUp = cmo.Value;

        // Continue with downtrend
        var downPrices = DescendingPrices(120m, 1m, 20);
        UpdatePrices(cmo, downPrices);
        var cmoAfterDown = cmo.Value;

        // CMO should decrease after downtrend
        Assert.True(cmoAfterDown < cmoAfterUp,
            $"CMO should decrease after downtrend: {cmoAfterUp} -> {cmoAfterDown}");
    }

    [Fact]
    public void CMO_DifferentPeriods_DifferentSensitivity()
    {
        var shortCmo = Indicators.CMO(5);
        var longCmo = Indicators.CMO(20);

        var prices = Prices(100m, 102m, 104m, 106m, 108m, 110m, 112m, 114m, 116m, 118m,
                           120m, 119m, 118m, 117m, 116m, 115m, 114m, 113m, 112m, 111m,
                           110m, 109m, 108m, 107m, 106m);

        UpdatePrices(shortCmo, prices);
        UpdatePrices(longCmo, prices);

        AssertReady(shortCmo);
        AssertReady(longCmo);

        // Both should be bounded
        AssertInRange(shortCmo.Value, -100m, 100m);
        AssertInRange(longCmo.Value, -100m, 100m);

        // Short period should be more responsive to recent changes
        Assert.NotEqual(shortCmo.Value, longCmo.Value);
    }

    [Fact]
    public void CMO_Count_IncrementsCorrectly()
    {
        var cmo = Indicators.CMO(14);

        Assert.Equal(0, cmo.Count);

        cmo.Update(100m);
        Assert.Equal(1, cmo.Count);

        cmo.Update(101m);
        Assert.Equal(2, cmo.Count);

        for (int i = 0; i < 15; i++)
        {
            cmo.Update(100m + i);
        }
        Assert.Equal(17, cmo.Count);
    }

    [Fact]
    public void CMO_ZeroPrices_HandlesGracefully()
    {
        var cmo = Indicators.CMO(14);

        TestZeroPrices(cmo, 20);

        // With zero prices (no change), CMO should be 0
        AssertApproximately(0m, cmo.Value, DefaultPrecision);
        AssertInRange(cmo.Value, -100m, 100m);
    }

    [Fact]
    public void CMO_LargePrices_NoOverflow()
    {
        var cmo = Indicators.CMO(14);

        // CMO needs period+1 prices to be ready
        var largePrices = new[] { 1000000m, 1000001m, 1000002m, 999999m, 1000000m,
                                  1000001m, 1000002m, 1000003m, 1000004m, 1000005m,
                                  1000006m, 1000007m, 1000008m, 1000009m, 1000010m };

        try
        {
            UpdatePrices(cmo, largePrices);
        }
        catch (OverflowException)
        {
            throw new Xunit.Sdk.XunitException("Indicator overflowed with large prices");
        }

        AssertReady(cmo);
        AssertInRange(cmo.Value, -100m, 100m);
    }

    [Fact]
    public void CMO_SimilarToRSI_ButDifferentScale()
    {
        var cmo = Indicators.CMO(14);
        var rsi = Indicators.RSI(14);

        var prices = AscendingPrices(100m, 1m, 30);

        UpdatePrices(cmo, prices);
        UpdatePrices(rsi, prices);

        AssertReady(cmo);
        AssertReady(rsi);

        // Both should show upward momentum
        // RSI is 0-100, CMO is -100 to +100
        // When RSI > 50, CMO should be > 0
        Assert.True(cmo.Value > 0m, "CMO should be positive for uptrend");
        Assert.True(rsi.Value > 50m, "RSI should be > 50 for uptrend");

        // Mathematical relationship: CMO ≈ 2 * (RSI - 50)
        var expectedCmo = 2m * (rsi.Value - 50m);
        AssertApproximately(expectedCmo, cmo.Value, 5m);
    }

    [Fact]
    public void CMO_StrongUptrend_HighPositiveValue()
    {
        var cmo = Indicators.CMO(10);

        // Very strong uptrend
        var prices = AscendingPrices(100m, 5m, 25);

        UpdatePrices(cmo, prices);
        AssertReady(cmo);

        // Should be strongly positive
        Assert.True(cmo.Value > 70m, $"CMO should be > 70 for strong uptrend, got {cmo.Value}");
    }

    [Fact]
    public void CMO_StrongDowntrend_HighNegativeValue()
    {
        var cmo = Indicators.CMO(10);

        // Very strong downtrend
        var prices = DescendingPrices(200m, 5m, 25);

        UpdatePrices(cmo, prices);
        AssertReady(cmo);

        // Should be strongly negative
        Assert.True(cmo.Value < -70m, $"CMO should be < -70 for strong downtrend, got {cmo.Value}");
    }

    [Fact]
    public void CMO_MeasuresNetMomentum()
    {
        var cmo = Indicators.CMO(10);

        // Mix of ups and downs, net positive
        var prices = Prices(100m, 103m, 102m, 105m, 104m, 107m, 106m, 109m, 108m, 111m, 110m, 113m);

        UpdatePrices(cmo, prices);
        AssertReady(cmo);

        // Net is upward, so CMO should be positive
        Assert.True(cmo.Value > 0m, "CMO should be positive for net upward movement");
        AssertInRange(cmo.Value, -100m, 100m);
    }

    [Fact]
    public void CMO_UpdatesWithEachNewPrice()
    {
        var cmo = Indicators.CMO(10);

        var prices = AscendingPrices(100m, 1m, 20);
        UpdatePrices(cmo, prices);
        var value1 = cmo.Value;

        cmo.Update(115m);
        var value2 = cmo.Value;

        // Value should update
        Assert.NotEqual(value1, value2);
        AssertInRange(value2, -100m, 100m);
    }

    [Fact]
    public void CMO_Symmetrical_PositiveAndNegativeRanges()
    {
        var cmoUp = Indicators.CMO(10);
        var cmoDown = Indicators.CMO(10);

        // Pure uptrend
        var upPrices = AscendingPrices(100m, 2m, 20);
        UpdatePrices(cmoUp, upPrices);

        // Pure downtrend (mirror)
        var downPrices = DescendingPrices(140m, 2m, 20);
        UpdatePrices(cmoDown, downPrices);

        AssertReady(cmoUp);
        AssertReady(cmoDown);

        // Values should be roughly symmetrical (opposite signs, similar magnitude)
        Assert.True(cmoUp.Value > 0m && cmoDown.Value < 0m,
            "Up and down trends should produce opposite signs");
        AssertApproximately(Math.Abs(cmoUp.Value), Math.Abs(cmoDown.Value), 10m);
    }
}
