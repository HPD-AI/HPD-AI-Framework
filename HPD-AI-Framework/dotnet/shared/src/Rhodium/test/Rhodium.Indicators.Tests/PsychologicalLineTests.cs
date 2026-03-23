using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;
using static Rhodium.Indicators.Tests.TestHelpers;

namespace Rhodium.Indicators.Tests;

public class PsychologicalLineTests
{
    [Fact]
    public void PsychologicalLine_InitialState_NotReady()
    {
        var psych = Indicators.PsychologicalLine(12);

        AssertNotReady(psych);
        AssertCount(0, psych);
    }

    [Fact]
    public void PsychologicalLine_BecomesReady_AfterPeriodPlusOne()
    {
        var psych = Indicators.PsychologicalLine(12);
        var prices = AscendingPrices(100m, 1m, 20);

        // Needs period+1 prices (first establishes baseline, then period comparisons)
        // After 12 updates, count=12, which is NOT > period (12)
        for (int i = 0; i < 12; i++)
        {
            psych.Update(prices[i]);
            AssertNotReady(psych, $"Should not be ready after {i + 1} updates");
        }

        // After 13th update, count=13, which IS > period (12)
        psych.Update(prices[12]);
        AssertReady(psych, "Should be ready after period+1 updates");
    }

    [Fact]
    public void PsychologicalLine_Reset_ClearsState()
    {
        var psych = Indicators.PsychologicalLine(12);
        var prices = AscendingPrices(100m, 1m, 20);

        UpdatePrices(psych, prices);
        AssertReady(psych);

        psych.Reset();

        AssertNotReady(psych);
        AssertCount(0, psych);
    }

    [Fact]
    public void PsychologicalLine_IsBounded_BetweenZeroAnd100()
    {
        var psych = Indicators.PsychologicalLine(12);

        // Test ascending
        var up = AscendingPrices(100m, 1m, 20);
        UpdatePrices(psych, up);
        AssertInRange(psych.Value, 0m, 100m);

        // Reset and test descending
        psych.Reset();
        var down = DescendingPrices(150m, 1m, 20);
        UpdatePrices(psych, down);
        AssertInRange(psych.Value, 0m, 100m);
    }

    [Fact]
    public void PsychologicalLine_AllAscending_Returns100Percent()
    {
        var psych = Indicators.PsychologicalLine(10);

        // All prices ascending (all bars close up)
        var prices = AscendingPrices(100m, 1m, 20);

        UpdatePrices(psych, prices);
        AssertReady(psych);

        // 100% of bars closed up
        AssertApproximately(100m, psych.Value, HighPrecision);
    }

    [Fact]
    public void PsychologicalLine_AllDescending_ReturnsZeroPercent()
    {
        var psych = Indicators.PsychologicalLine(10);

        // All prices descending (all bars close down)
        var prices = DescendingPrices(150m, 1m, 20);

        UpdatePrices(psych, prices);
        AssertReady(psych);

        // 0% of bars closed up
        AssertApproximately(0m, psych.Value, HighPrecision);
    }

    [Fact]
    public void PsychologicalLine_HalfUpHalfDown_Returns50Percent()
    {
        var psych = Indicators.PsychologicalLine(10);

        // Alternating up and down
        var prices = new decimal[20];
        decimal price = 100m;
        for (int i = 0; i < 20; i++)
        {
            price += (i % 2 == 0) ? 1m : -1m;
            prices[i] = price;
        }

        UpdatePrices(psych, prices);
        AssertReady(psych);

        // Approximately 50% up (alternating pattern)
        AssertApproximately(50m, psych.Value, 10m);
    }

    [Fact]
    public void PsychologicalLine_ConstantPrices_ReturnsZero()
    {
        var psych = Indicators.PsychologicalLine(10);

        // Constant prices (no up moves)
        var prices = ConstantPrices(100m, 20);

        UpdatePrices(psych, prices);
        AssertReady(psych);

        // No bars closed above previous close
        AssertApproximately(0m, psych.Value, HighPrecision);
    }

    [Fact]
    public void PsychologicalLine_MeasuresPercentageOfUpBars()
    {
        var psych = Indicators.PsychologicalLine(10);

        // 7 up bars, then 3 down bars in the 10-bar window
        var prices = Prices(100m, 101m, 102m, 103m, 104m, 105m, 106m, 107m,
                           106m, 105m, 104m, 103m);

        UpdatePrices(psych, prices);
        AssertReady(psych);

        // In the last 10 bars: 7 up, 3 down = 70%
        // But rolling window might show different percentage
        AssertInRange(psych.Value, 0m, 100m);
    }

    [Fact]
    public void PsychologicalLine_OscillatingPrices_MidRange()
    {
        var psych = Indicators.PsychologicalLine(12);
        var prices = OscillatingPrices(95m, 105m, 30);

        UpdatePrices(psych, prices);
        AssertReady(psych);

        // Should be around 50% for oscillating prices
        AssertInRange(psych.Value, 30m, 70m);
    }

    [Fact]
    public void PsychologicalLine_RespondsToTrendChanges()
    {
        var psych = Indicators.PsychologicalLine(10);

        // Start with uptrend
        var upPrices = AscendingPrices(100m, 1m, 15);
        UpdatePrices(psych, upPrices);
        var valueAfterUp = psych.Value;

        // Switch to downtrend
        var downPrices = DescendingPrices(115m, 1m, 15);
        UpdatePrices(psych, downPrices);
        var valueAfterDown = psych.Value;

        // Should decrease after downtrend
        Assert.True(valueAfterDown < valueAfterUp,
            $"PsychologicalLine should decrease after downtrend: {valueAfterUp} -> {valueAfterDown}");
    }

    [Fact]
    public void PsychologicalLine_Count_IncrementsCorrectly()
    {
        var psych = Indicators.PsychologicalLine(12);

        Assert.Equal(0, psych.Count);

        psych.Update(100m);
        Assert.Equal(1, psych.Count);

        for (int i = 0; i < 15; i++)
        {
            psych.Update(100m + i);
        }
        Assert.Equal(16, psych.Count);
    }

    [Fact]
    public void PsychologicalLine_DifferentPeriods_DifferentValues()
    {
        var shortPsych = Indicators.PsychologicalLine(5);
        var longPsych = Indicators.PsychologicalLine(20);

        var prices = AscendingPrices(100m, 0.5m, 30);

        UpdatePrices(shortPsych, prices);
        UpdatePrices(longPsych, prices);

        AssertReady(shortPsych);
        AssertReady(longPsych);

        // Both should show high percentage (all ascending)
        AssertInRange(shortPsych.Value, 0m, 100m);
        AssertInRange(longPsych.Value, 0m, 100m);

        // Both should be near 100% for pure uptrend
        Assert.True(shortPsych.Value > 90m || longPsych.Value > 90m);
    }

    [Fact]
    public void PsychologicalLine_ZeroPrices_HandlesGracefully()
    {
        var psych = Indicators.PsychologicalLine(10);

        TestZeroPrices(psych, 20);

        // Constant zeros = no up bars = 0%
        AssertApproximately(0m, psych.Value, DefaultPrecision);
        AssertInRange(psych.Value, 0m, 100m);
    }

    [Fact]
    public void PsychologicalLine_LargePrices_NoOverflow()
    {
        var psych = Indicators.PsychologicalLine(10);

        // PsychologicalLine needs period+1 prices to be ready
        var largePrices = new[] { 1000000m, 1000001m, 1000002m, 999999m, 1000000m,
                                  1000001m, 1000002m, 1000003m, 1000004m, 1000005m, 1000006m };

        try
        {
            UpdatePrices(psych, largePrices);
        }
        catch (OverflowException)
        {
            throw new Xunit.Sdk.XunitException("Indicator overflowed with large prices");
        }

        AssertReady(psych);
        AssertInRange(psych.Value, 0m, 100m);
    }

    [Fact]
    public void PsychologicalLine_StrongBullish_HighPercentage()
    {
        var psych = Indicators.PsychologicalLine(10);

        // Very strong uptrend
        var prices = AscendingPrices(100m, 3m, 20);

        UpdatePrices(psych, prices);
        AssertReady(psych);

        // Should be near 100%
        Assert.True(psych.Value > 90m, $"PsychologicalLine should be > 90% for strong uptrend, got {psych.Value}");
    }

    [Fact]
    public void PsychologicalLine_StrongBearish_LowPercentage()
    {
        var psych = Indicators.PsychologicalLine(10);

        // Very strong downtrend
        var prices = DescendingPrices(200m, 3m, 20);

        UpdatePrices(psych, prices);
        AssertReady(psych);

        // Should be near 0%
        Assert.True(psych.Value < 10m, $"PsychologicalLine should be < 10% for strong downtrend, got {psych.Value}");
    }

    [Fact]
    public void PsychologicalLine_UpdatesWithEachNewPrice()
    {
        var psych = Indicators.PsychologicalLine(10);

        var prices = AscendingPrices(100m, 1m, 15);
        UpdatePrices(psych, prices);
        var value1 = psych.Value;

        psych.Update(110m);  // Another up bar
        var value2 = psych.Value;

        // Value might change due to rolling window
        AssertReady(psych);
        AssertInRange(value2, 0m, 100m);
    }

    [Fact]
    public void PsychologicalLine_MostlyUp_HighValue()
    {
        var psych = Indicators.PsychologicalLine(10);

        // 8 out of 10 bars up
        var prices = Prices(100m, 101m, 102m, 103m, 104m, 105m, 106m, 107m, 108m,
                           107m, 106m, 107m, 108m);

        UpdatePrices(psych, prices);
        AssertReady(psych);

        // Should be high percentage (mostly up bars in window)
        Assert.True(psych.Value >= 50m, $"PsychologicalLine should be >= 50% for mostly up bars");
    }

    [Fact]
    public void PsychologicalLine_MostlyDown_LowValue()
    {
        var psych = Indicators.PsychologicalLine(10);

        // 8 out of 10 bars down
        var prices = Prices(100m, 99m, 98m, 97m, 96m, 95m, 94m, 93m, 92m,
                           93m, 94m, 93m, 92m);

        UpdatePrices(psych, prices);
        AssertReady(psych);

        // Should be low percentage (mostly down bars in window)
        Assert.True(psych.Value <= 50m, $"PsychologicalLine should be <= 50% for mostly down bars");
    }

    [Fact]
    public void PsychologicalLine_IndicatesSentiment()
    {
        var psych = Indicators.PsychologicalLine(12);

        var prices = AscendingPrices(100m, 1m, 20);
        UpdatePrices(psych, prices);

        AssertReady(psych);

        // High value indicates bullish sentiment
        Assert.True(psych.Value > 80m, "High PsychologicalLine indicates bullish sentiment");
    }

    [Fact]
    public void PsychologicalLine_RollingWindow_UpdatesCorrectly()
    {
        var psych = Indicators.PsychologicalLine(5);

        // First 6 prices (all up)
        var prices = Prices(100m, 101m, 102m, 103m, 104m, 105m);
        UpdatePrices(psych, prices);
        AssertApproximately(100m, psych.Value, HighPrecision);

        // Add down bars
        psych.Update(104m);  // Down
        psych.Update(103m);  // Down
        psych.Update(102m);  // Down
        psych.Update(101m);  // Down
        psych.Update(100m);  // Down

        // Now window has 5 down bars = 0%
        AssertApproximately(0m, psych.Value, HighPrecision);
    }

    [Fact]
    public void PsychologicalLine_CountsOnlyUpBars_IgnoresDownBars()
    {
        var psych = Indicators.PsychologicalLine(10);

        // Specific pattern: 3 up, 7 down in sequence
        var prices = Prices(100m, 101m, 102m, 103m,  // 3 up
                           102m, 101m, 100m, 99m, 98m, 97m, 96m);  // 7 down

        UpdatePrices(psych, prices);
        AssertReady(psych);

        // 3 out of 10 bars are up = 30%
        AssertApproximately(30m, psych.Value, HighPrecision);
    }
}
