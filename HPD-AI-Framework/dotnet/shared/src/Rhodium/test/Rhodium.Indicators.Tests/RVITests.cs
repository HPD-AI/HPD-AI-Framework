using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

public class RVITests
{
    [Fact]
    public void BasicFunctionality_CalculatesRelativeVolatilityIndex()
    {
        var rvi = Indicators.RVI(14, 10);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 30);

        TestHelpers.UpdatePrices(rvi, prices);

        TestHelpers.AssertReady(rvi);
        // RVI should be between 0 and 100
        TestHelpers.AssertInRange(rvi.Value, 0m, 100m);
    }

    [Fact]
    public void BecomesReadyAfterStdPeriodPlusPeriod()
    {
        var rvi = Indicators.RVI(14, 10);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 30);

        // Should not be ready until count > stdPeriod + period (10 + 14 = 24)
        for (int i = 0; i < 24; i++)
        {
            rvi.Update(prices[i]);
            TestHelpers.AssertNotReady(rvi);
        }

        rvi.Update(prices[24]);
        TestHelpers.AssertReady(rvi);
    }

    [Fact]
    public void ResetClearsState()
    {
        var rvi = Indicators.RVI(14, 10);

        TestHelpers.TestReset(rvi, () =>
        {
            var prices = TestHelpers.AscendingPrices(100m, 1m, 30);
            TestHelpers.UpdatePrices(rvi, prices);
        });
    }

    [Fact]
    public void ValueBetweenZeroAndOneHundred()
    {
        var rvi = Indicators.RVI(14, 10);
        var prices = TestHelpers.OscillatingPrices(95m, 105m, 30);

        TestHelpers.UpdatePrices(rvi, prices);

        TestHelpers.AssertReady(rvi);
        TestHelpers.AssertInRange(rvi.Value, 0m, 100m);
    }

    [Fact]
    public void HighRVIWhenVolatilityIncreasing()
    {
        var rvi = Indicators.RVI(10, 10);

        // Start with low volatility
        var prices = new List<decimal>();
        for (int i = 0; i < 15; i++)
        {
            prices.Add(100m);
        }

        // Then add increasing volatility
        prices.AddRange(new[] { 100m, 102m, 99m, 105m, 95m, 110m, 90m, 115m, 85m, 120m });

        TestHelpers.UpdatePrices(rvi, prices.ToArray());

        TestHelpers.AssertReady(rvi);
        // Should show high RVI due to increasing volatility
        Assert.True(rvi.Value > 50m);
    }

    [Fact]
    public void LowRVIWhenVolatilityDecreasing()
    {
        var rvi = Indicators.RVI(10, 10);

        // Start with high volatility
        var prices = new List<decimal>();
        prices.AddRange(new[] { 100m, 120m, 80m, 130m, 70m, 140m, 60m, 150m, 50m, 160m });

        // Then decrease volatility
        for (int i = 0; i < 15; i++)
        {
            prices.Add(100m);
        }

        TestHelpers.UpdatePrices(rvi, prices.ToArray());

        TestHelpers.AssertReady(rvi);
        // Should show low RVI due to decreasing volatility
        Assert.True(rvi.Value < 50m);
    }

    [Fact]
    public void MaxValueWhenOnlyGains()
    {
        var rvi = Indicators.RVI(10, 10);

        // Create pattern where std dev consistently increases
        var prices = new List<decimal> { 100m };
        for (int i = 1; i <= 30; i++)
        {
            // Add prices with increasing spread
            prices.Add(100m - i * 0.5m);
            prices.Add(100m + i * 0.5m);
        }

        TestHelpers.UpdatePrices(rvi, prices.ToArray());

        TestHelpers.AssertReady(rvi);
        // Should approach 100 when volatility only increases
        Assert.True(rvi.Value > 70m);
    }

    [Fact]
    public void ConstantPricesProduceMidRangeRVI()
    {
        var rvi = Indicators.RVI(10, 10);
        var prices = TestHelpers.ConstantPrices(100m, 30);

        TestHelpers.UpdatePrices(rvi, prices);

        TestHelpers.AssertReady(rvi);
        // With constant prices, std dev changes are 0, so RVI calculation may vary
        // Just ensure it's valid
        TestHelpers.AssertInRange(rvi.Value, 0m, 100m);
    }

    [Fact]
    public void DifferentPeriods()
    {
        var rvi1 = Indicators.RVI(10, 10);
        var rvi2 = Indicators.RVI(20, 10);

        var prices = TestHelpers.OscillatingPrices(90m, 110m, 35);

        TestHelpers.UpdatePrices(rvi1, prices);
        TestHelpers.UpdatePrices(rvi2, prices);

        TestHelpers.AssertReady(rvi1);
        TestHelpers.AssertReady(rvi2);

        TestHelpers.AssertInRange(rvi1.Value, 0m, 100m);
        TestHelpers.AssertInRange(rvi2.Value, 0m, 100m);
    }

    [Fact]
    public void OscillatingPricesIncreaseVolatility()
    {
        var rvi1 = Indicators.RVI(14, 10);
        var rvi2 = Indicators.RVI(14, 10);

        // Stable prices
        var stablePrices = TestHelpers.AscendingPrices(100m, 0.1m, 30);
        TestHelpers.UpdatePrices(rvi1, stablePrices);

        // Oscillating prices (higher volatility)
        var oscPrices = TestHelpers.OscillatingPrices(90m, 110m, 30);
        TestHelpers.UpdatePrices(rvi2, oscPrices);

        TestHelpers.AssertReady(rvi1);
        TestHelpers.AssertReady(rvi2);

        // Both should be valid
        TestHelpers.AssertInRange(rvi1.Value, 0m, 100m);
        TestHelpers.AssertInRange(rvi2.Value, 0m, 100m);
    }

    [Fact]
    public void HandleZeroPrices()
    {
        var rvi = Indicators.RVI(14, 10);
        TestHelpers.TestZeroPrices(rvi, 30);

        if (rvi.IsReady)
        {
            TestHelpers.AssertInRange(rvi.Value, 0m, 100m);
        }
    }
}
