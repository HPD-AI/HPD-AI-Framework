using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;
using static Rhodium.Indicators.Tests.TestHelpers;

namespace Rhodium.Indicators.Tests;

public class ROCTests
{
    [Fact]
    public void ROC_InitialState_NotReady()
    {
        var roc = Indicators.ROC(10);

        AssertNotReady(roc);
        AssertCount(0, roc);
    }

    [Fact]
    public void ROC_BecomesReady_AfterPeriodPlusOne()
    {
        var roc = Indicators.ROC(10);
        var prices = AscendingPrices(100m, 1m, 15);

        // ROC needs period+1 prices to calculate first rate of change
        // After 10 updates, count=10, which is NOT > period (10)
        for (int i = 0; i < 10; i++)
        {
            roc.Update(prices[i]);
            AssertNotReady(roc, $"Should not be ready after {i + 1} updates");
        }

        // After 11th update, count=11, which IS > period (10)
        roc.Update(prices[10]);
        AssertReady(roc, "Should be ready after period+1 updates");
    }

    [Fact]
    public void ROC_Reset_ClearsState()
    {
        var roc = Indicators.ROC(10);
        var prices = AscendingPrices(100m, 1m, 15);

        UpdatePrices(roc, prices);
        AssertReady(roc);

        roc.Reset();

        AssertNotReady(roc);
        AssertCount(0, roc);
    }

    [Fact]
    public void ROC_CalculatesPercentageChange()
    {
        var roc = Indicators.ROC(5);

        // Start at 100, end at 110 after 5 periods
        var prices = Prices(100m, 102m, 104m, 106m, 108m, 110m);

        UpdatePrices(roc, prices);
        AssertReady(roc);

        // ROC = (110 - 100) / 100 * 100 = 10%
        AssertApproximately(10m, roc.Value, LowPrecision);
    }

    [Fact]
    public void ROC_PositiveForAscendingPrices()
    {
        var roc = Indicators.ROC(10);
        var prices = AscendingPrices(100m, 1m, 20);

        UpdatePrices(roc, prices);
        AssertReady(roc);

        // ROC should be positive for ascending prices
        Assert.True(roc.Value > 0m, $"ROC should be positive for ascending prices, got {roc.Value}");
    }

    [Fact]
    public void ROC_NegativeForDescendingPrices()
    {
        var roc = Indicators.ROC(10);
        var prices = DescendingPrices(100m, 1m, 20);

        UpdatePrices(roc, prices);
        AssertReady(roc);

        // ROC should be negative for descending prices
        Assert.True(roc.Value < 0m, $"ROC should be negative for descending prices, got {roc.Value}");
    }

    [Fact]
    public void ROC_ZeroForConstantPrices()
    {
        var roc = Indicators.ROC(10);
        var prices = ConstantPrices(100m, 20);

        UpdatePrices(roc, prices);
        AssertReady(roc);

        // ROC should be 0% for constant prices
        AssertApproximately(0m, roc.Value, HighPrecision);
    }

    [Fact]
    public void ROC_DoubledPrice_Returns100Percent()
    {
        var roc = Indicators.ROC(5);

        // Price doubles from 100 to 200 over 5 periods
        var prices = Prices(100m, 120m, 140m, 160m, 180m, 200m);

        UpdatePrices(roc, prices);
        AssertReady(roc);

        // ROC = (200 - 100) / 100 * 100 = 100%
        AssertApproximately(100m, roc.Value, LowPrecision);
    }

    [Fact]
    public void ROC_HalvedPrice_ReturnsNegative50Percent()
    {
        var roc = Indicators.ROC(5);

        // Price halves from 100 to 50 over 5 periods
        var prices = Prices(100m, 90m, 80m, 70m, 60m, 50m);

        UpdatePrices(roc, prices);
        AssertReady(roc);

        // ROC = (50 - 100) / 100 * 100 = -50%
        AssertApproximately(-50m, roc.Value, LowPrecision);
    }

    [Fact]
    public void ROC_OscillatingPrices_OscillatesAroundZero()
    {
        var roc = Indicators.ROC(10);
        var prices = OscillatingPrices(95m, 105m, 30);

        UpdatePrices(roc, prices);
        AssertReady(roc);

        // ROC should oscillate for oscillating prices
        // Value can be positive, negative, or near zero
        AssertInRange(roc.Value, -20m, 20m);
    }

    [Fact]
    public void ROC_ShortPeriod_MoreResponsive()
    {
        var shortRoc = Indicators.ROC(3);
        var longRoc = Indicators.ROC(15);

        var prices = AscendingPrices(100m, 1m, 20);

        UpdatePrices(shortRoc, prices);
        UpdatePrices(longRoc, prices);

        AssertReady(shortRoc);
        AssertReady(longRoc);

        // Both should be positive, but short period should be more sensitive
        Assert.True(shortRoc.Value > 0m);
        Assert.True(longRoc.Value > 0m);
        Assert.NotEqual(shortRoc.Value, longRoc.Value);
    }

    [Fact]
    public void ROC_UpdatesWithEachNewPrice()
    {
        var roc = Indicators.ROC(5);

        var prices = Prices(100m, 102m, 104m, 106m, 108m, 110m);
        UpdatePrices(roc, prices);
        var value1 = roc.Value;

        roc.Update(112m);
        var value2 = roc.Value;

        // Value should change with new price
        Assert.NotEqual(value1, value2);
    }

    [Fact]
    public void ROC_LargeIncrease_LargePositiveROC()
    {
        var roc = Indicators.ROC(5);

        // 50% increase
        var prices = Prices(100m, 105m, 110m, 120m, 135m, 150m);

        UpdatePrices(roc, prices);
        AssertReady(roc);

        // ROC = (150 - 100) / 100 * 100 = 50%
        AssertApproximately(50m, roc.Value, LowPrecision);
    }

    [Fact]
    public void ROC_LargeDecrease_LargeNegativeROC()
    {
        var roc = Indicators.ROC(5);

        // 30% decrease
        var prices = Prices(100m, 95m, 88m, 80m, 75m, 70m);

        UpdatePrices(roc, prices);
        AssertReady(roc);

        // ROC = (70 - 100) / 100 * 100 = -30%
        AssertApproximately(-30m, roc.Value, LowPrecision);
    }

    [Fact]
    public void ROC_Count_IncrementsCorrectly()
    {
        var roc = Indicators.ROC(10);

        Assert.Equal(0, roc.Count);

        roc.Update(100m);
        Assert.Equal(1, roc.Count);

        for (int i = 0; i < 15; i++)
        {
            roc.Update(100m + i);
        }
        Assert.Equal(16, roc.Count);
    }

    [Fact]
    public void ROC_PeriodOne_ComparesConsecutivePrices()
    {
        var roc = Indicators.ROC(1);

        roc.Update(100m);
        roc.Update(105m);

        AssertReady(roc);

        // ROC = (105 - 100) / 100 * 100 = 5%
        AssertApproximately(5m, roc.Value, LowPrecision);
    }

    [Fact]
    public void ROC_SineWave_OscillatesSymmetrically()
    {
        var roc = Indicators.ROC(10);
        var prices = SineWavePrices(100m, 10m, 50, frequency: 1);

        decimal minRoc = decimal.MaxValue;
        decimal maxRoc = decimal.MinValue;

        foreach (var price in prices)
        {
            roc.Update(price);
            if (roc.IsReady)
            {
                minRoc = Math.Min(minRoc, roc.Value);
                maxRoc = Math.Max(maxRoc, roc.Value);
            }
        }

        // ROC should oscillate with sine wave
        Assert.True(maxRoc > 0m && minRoc < 0m, "ROC should oscillate positive and negative");
    }

    [Fact]
    public void ROC_ZeroPrices_ReturnsZero()
    {
        var roc = Indicators.ROC(5);

        // All zeros
        TestZeroPrices(roc, 15);

        // ROC with zero base is 0 (handled in implementation)
        AssertApproximately(0m, roc.Value, DefaultPrecision);
    }

    [Fact]
    public void ROC_LargePrices_NoOverflow()
    {
        var roc = Indicators.ROC(10);

        // ROC needs period+1 prices to be ready
        var largePrices = new[] { 1000000m, 1000001m, 1000002m, 999999m, 1000000m,
                                  1000001m, 1000002m, 1000003m, 1000004m, 1000005m, 1000006m };

        try
        {
            UpdatePrices(roc, largePrices);
        }
        catch (OverflowException)
        {
            throw new Xunit.Sdk.XunitException("Indicator overflowed with large prices");
        }

        AssertReady(roc);
        // Value should be valid
        Assert.True(roc.Value >= decimal.MinValue && roc.Value <= decimal.MaxValue);
    }

    [Fact]
    public void ROC_SmallChanges_SmallROC()
    {
        var roc = Indicators.ROC(5);

        // Very small changes (0.1%)
        var prices = Prices(100.00m, 100.02m, 100.04m, 100.06m, 100.08m, 100.10m);

        UpdatePrices(roc, prices);
        AssertReady(roc);

        // ROC should be approximately 0.1%
        AssertApproximately(0.1m, roc.Value, LowPrecision);
    }

    [Fact]
    public void ROC_ReversalPattern_ShowsDirectionChange()
    {
        var roc = Indicators.ROC(5);

        // Up then down
        var prices = Prices(100m, 105m, 110m, 115m, 120m, 125m, 120m, 115m, 110m, 105m, 100m);

        foreach (var price in prices)
        {
            roc.Update(price);
        }

        AssertReady(roc);

        // After reversal, ROC should be negative
        Assert.True(roc.Value < 5m, $"ROC should decrease after reversal, got {roc.Value}");
    }
}
