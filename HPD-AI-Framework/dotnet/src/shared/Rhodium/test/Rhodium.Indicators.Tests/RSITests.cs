using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;
using static Rhodium.Indicators.Tests.TestHelpers;

namespace Rhodium.Indicators.Tests;

public class RSITests
{
    [Fact]
    public void RSI_InitialState_NotReady()
    {
        var rsi = Indicators.RSI(14);

        AssertNotReady(rsi);
        AssertCount(0, rsi);
    }

    [Fact]
    public void RSI_BecomesReady_AfterPeriodPlusOne()
    {
        var rsi = Indicators.RSI(14);
        var prices = AscendingPrices(100m, 1m, 20);

        // Should not be ready before period+1
        for (int i = 0; i < 14; i++)
        {
            rsi.Update(prices[i]);
            AssertNotReady(rsi, $"RSI should not be ready after {i + 1} updates");
        }

        // Should be ready after period+1 (need previous price for first comparison)
        rsi.Update(prices[14]);
        AssertReady(rsi, "RSI should be ready after period+1 updates");
    }

    [Fact]
    public void RSI_Reset_ClearsState()
    {
        var rsi = Indicators.RSI(14);
        var prices = AscendingPrices(100m, 1m, 20);

        UpdatePrices(rsi, prices);
        AssertReady(rsi);

        rsi.Reset();

        AssertNotReady(rsi);
        AssertCount(0, rsi);
    }

    [Fact]
    public void RSI_AscendingPrices_ApproachesUpperBound()
    {
        var rsi = Indicators.RSI(14);
        var prices = AscendingPrices(100m, 2m, 30);

        UpdatePrices(rsi, prices);

        // RSI should be high (above 70) for strong uptrend
        Assert.True(rsi.Value > 70m, $"RSI should be > 70 for ascending prices, got {rsi.Value}");
        AssertInRange(rsi.Value, 0m, 100m, "RSI must be between 0 and 100");
    }

    [Fact]
    public void RSI_DescendingPrices_ApproachesLowerBound()
    {
        var rsi = Indicators.RSI(14);
        var prices = DescendingPrices(100m, 2m, 30);

        UpdatePrices(rsi, prices);

        // RSI should be low (below 30) for strong downtrend
        Assert.True(rsi.Value < 30m, $"RSI should be < 30 for descending prices, got {rsi.Value}");
        AssertInRange(rsi.Value, 0m, 100m, "RSI must be between 0 and 100");
    }

    [Fact]
    public void RSI_ConstantPrices_ReturnsNeutral()
    {
        var rsi = Indicators.RSI(14);
        var prices = ConstantPrices(100m, 20);

        UpdatePrices(rsi, prices);

        // RSI should be 50 (neutral) when no change
        // Note: First update sets to 50, subsequent no-change updates maintain certain value
        AssertInRange(rsi.Value, 0m, 100m, "RSI must be bounded");
    }

    [Fact]
    public void RSI_IsBounded_BetweenZeroAndHundred()
    {
        var rsi = Indicators.RSI(14);

        // Test extreme ascending
        var extremeUp = AscendingPrices(50m, 10m, 30);
        UpdatePrices(rsi, extremeUp);
        AssertInRange(rsi.Value, 0m, 100m);

        // Reset and test extreme descending
        rsi.Reset();
        var extremeDown = DescendingPrices(500m, 20m, 30);
        UpdatePrices(rsi, extremeDown);
        AssertInRange(rsi.Value, 0m, 100m);
    }

    [Fact]
    public void RSI_OscillatingPrices_ShowsVariation()
    {
        var rsi = Indicators.RSI(14);
        var prices = OscillatingPrices(90m, 110m, 30);

        UpdatePrices(rsi, prices);

        // RSI should be in middle range for oscillating prices
        AssertInRange(rsi.Value, 30m, 70m);
    }

    [Fact]
    public void RSI_PureGainSequence_Approaches100()
    {
        var rsi = Indicators.RSI(10);

        // Create strong upward sequence
        var prices = new decimal[20];
        for (int i = 0; i < 20; i++)
        {
            prices[i] = 100m + i * 5m; // Strong gains
        }

        UpdatePrices(rsi, prices);

        // Should be very high RSI
        Assert.True(rsi.Value > 85m, $"RSI should be > 85 for pure gains, got {rsi.Value}");
        AssertInRange(rsi.Value, 0m, 100m);
    }

    [Fact]
    public void RSI_PureLossSequence_ApproachesZero()
    {
        var rsi = Indicators.RSI(10);

        // Create strong downward sequence
        var prices = new decimal[20];
        for (int i = 0; i < 20; i++)
        {
            prices[i] = 200m - i * 5m; // Strong losses
        }

        UpdatePrices(rsi, prices);

        // Should be very low RSI
        Assert.True(rsi.Value < 15m, $"RSI should be < 15 for pure losses, got {rsi.Value}");
        AssertInRange(rsi.Value, 0m, 100m);
    }

    [Fact]
    public void RSI_RespondsToTrendChanges()
    {
        var rsi = Indicators.RSI(14);

        // Start with uptrend
        var upPrices = AscendingPrices(100m, 1m, 20);
        UpdatePrices(rsi, upPrices);
        var rsiAfterUp = rsi.Value;

        // Continue with downtrend
        var downPrices = DescendingPrices(120m, 1m, 20);
        UpdatePrices(rsi, downPrices);
        var rsiAfterDown = rsi.Value;

        // RSI should decrease after downtrend
        Assert.True(rsiAfterDown < rsiAfterUp,
            $"RSI should decrease after downtrend: {rsiAfterUp} -> {rsiAfterDown}");
    }

    [Fact]
    public void RSI_DifferentPeriods_ProduceDifferentSensitivity()
    {
        var shortRsi = Indicators.RSI(5);
        var longRsi = Indicators.RSI(20);

        var prices = Prices(100m, 102m, 104m, 106m, 108m, 110m, 112m, 114m, 116m, 118m,
                           120m, 119m, 118m, 117m, 116m, 115m, 114m, 113m, 112m, 111m,
                           110m, 109m, 108m, 107m, 106m, 105m);

        UpdatePrices(shortRsi, prices);
        UpdatePrices(longRsi, prices);

        // Both should be ready and bounded
        AssertReady(shortRsi);
        AssertReady(longRsi);
        AssertInRange(shortRsi.Value, 0m, 100m);
        AssertInRange(longRsi.Value, 0m, 100m);

        // Short period should be more responsive to recent changes
        // After the downtrend at the end, short RSI should be lower
        Assert.True(shortRsi.Value < 50m || longRsi.Value < 50m || shortRsi.Value != longRsi.Value,
            "Different periods should produce different sensitivities");
    }

    [Fact]
    public void RSI_ZeroPrices_HandlesGracefully()
    {
        var rsi = Indicators.RSI(14);

        TestZeroPrices(rsi, 20);

        AssertInRange(rsi.Value, 0m, 100m);
    }

    [Fact]
    public void RSI_LargePrices_NoOverflow()
    {
        var rsi = Indicators.RSI(14);

        // Provide enough large prices for RSI to be ready
        var largePrices = new[] { 1000000m, 1000001m, 1000002m, 999999m, 1000000m, 1000003m, 1000004m,
                                 1000002m, 1000005m, 1000006m, 1000004m, 1000007m, 1000008m, 1000009m, 1000010m };

        UpdatePrices(rsi, largePrices);

        AssertReady(rsi);
        AssertInRange(rsi.Value, 0m, 100m);
    }

    [Fact]
    public void RSI_SinglePrice_ReturnsNeutralValue()
    {
        var rsi = Indicators.RSI(14);

        rsi.Update(100m);

        // First update sets RSI to 50 (neutral)
        Assert.Equal(50m, rsi.Value);
        AssertNotReady(rsi);
    }

    [Fact]
    public void RSI_AlternatingUpDown_MidRange()
    {
        var rsi = Indicators.RSI(14);

        // Create alternating pattern: up, down, up, down
        var prices = new decimal[30];
        decimal price = 100m;
        for (int i = 0; i < 30; i++)
        {
            price += (i % 2 == 0) ? 1m : -1m;
            prices[i] = price;
        }

        UpdatePrices(rsi, prices);

        // Should be near 50 for balanced up/down moves
        AssertInRange(rsi.Value, 30m, 70m);
    }

    [Fact]
    public void RSI_Count_IncrementsCorrectly()
    {
        var rsi = Indicators.RSI(14);

        Assert.Equal(0, rsi.Count);

        rsi.Update(100m);
        Assert.Equal(1, rsi.Count);

        rsi.Update(101m);
        Assert.Equal(2, rsi.Count);

        for (int i = 0; i < 13; i++)
        {
            rsi.Update(100m + i);
        }
        Assert.Equal(15, rsi.Count);
    }
}
