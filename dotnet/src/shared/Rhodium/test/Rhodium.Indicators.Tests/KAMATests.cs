using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

public class KAMATests
{
    [Fact]
    public void BasicFunctionality_AdaptsToMarketConditions()
    {
        var kama = Indicators.KAMA(10, 2, 30);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 20);

        TestHelpers.UpdatePrices(kama, prices);

        TestHelpers.AssertReady(kama);
        Assert.True(kama.Value > 0m);
    }

    [Fact]
    public void BecomesReadyAfterPeriod()
    {
        var kama = Indicators.KAMA(14, 2, 30);
        var prices = TestHelpers.AscendingPrices(100m, 1m, 20);

        TestHelpers.TestReadinessAfterPeriod(kama, 14, prices);
    }

    [Fact]
    public void ResetClearsState()
    {
        var kama = Indicators.KAMA(10, 2, 30);

        TestHelpers.TestReset(kama, () =>
        {
            var prices = TestHelpers.AscendingPrices(100m, 1m, 15);
            TestHelpers.UpdatePrices(kama, prices);
        });
    }

    [Fact]
    public void FirstValueEqualsFirstPrice()
    {
        var kama = Indicators.KAMA(10, 2, 30);

        kama.Update(100m);

        Assert.Equal(100m, kama.Value);
    }

    [Fact]
    public void TracksStrongTrendQuickly()
    {
        var kama = Indicators.KAMA(10, 2, 30);

        // Strong uptrend
        var prices = TestHelpers.AscendingPrices(100m, 2m, 20);
        TestHelpers.UpdatePrices(kama, prices);

        TestHelpers.AssertReady(kama);

        // KAMA should track trend closely
        // After strong trend, KAMA should be close to latest price
        var lastPrice = prices[^1];
        var difference = Math.Abs(kama.Value - lastPrice);

        // Should be reasonably close
        Assert.True(difference < 10m);
    }

    [Fact]
    public void SmoothsNoisyPrices()
    {
        var kama = Indicators.KAMA(10, 2, 30);

        // Noisy prices oscillating around 100
        var prices = TestHelpers.OscillatingPrices(95m, 105m, 25);
        TestHelpers.UpdatePrices(kama, prices);

        TestHelpers.AssertReady(kama);

        // KAMA should smooth out noise and stay near center
        TestHelpers.AssertInRange(kama.Value, 90m, 110m);
    }

    [Fact]
    public void AdaptsSlowerInRangingMarket()
    {
        var kama = Indicators.KAMA(10, 2, 30);

        // Ranging market (sideways)
        var prices = new List<decimal>();
        for (int i = 0; i < 20; i++)
        {
            prices.Add(100m + (i % 2 == 0 ? 1m : -1m));
        }

        TestHelpers.UpdatePrices(kama, prices.ToArray());

        TestHelpers.AssertReady(kama);

        // In ranging market, KAMA should be relatively stable
        TestHelpers.AssertInRange(kama.Value, 95m, 105m);
    }

    [Fact]
    public void AdaptsFasterInTrendingMarket()
    {
        var kama = Indicators.KAMA(10, 2, 30);

        // Establish baseline
        var baseline = TestHelpers.ConstantPrices(100m, 15);
        TestHelpers.UpdatePrices(kama, baseline);

        var kamaBeforeTrend = kama.Value;

        // Strong trend
        var trend = TestHelpers.AscendingPrices(100m, 5m, 10);
        TestHelpers.UpdatePrices(kama, trend);

        // KAMA should have moved significantly
        Assert.True(kama.Value > kamaBeforeTrend + 10m);
    }

    [Fact]
    public void DifferentFastSlowPeriods()
    {
        var kama1 = Indicators.KAMA(10, 2, 30);   // Standard
        var kama2 = Indicators.KAMA(10, 2, 10);   // Faster slow period
        var kama3 = Indicators.KAMA(10, 5, 30);   // Slower fast period

        var prices = TestHelpers.AscendingPrices(100m, 1m, 20);

        TestHelpers.UpdatePrices(kama1, prices);
        TestHelpers.UpdatePrices(kama2, prices);
        TestHelpers.UpdatePrices(kama3, prices);

        TestHelpers.AssertReady(kama1);
        TestHelpers.AssertReady(kama2);
        TestHelpers.AssertReady(kama3);

        // All should be tracking the trend but with different responsiveness
        Assert.True(kama1.Value > 100m);
        Assert.True(kama2.Value > 100m);
        Assert.True(kama3.Value > 100m);
    }

    [Fact]
    public void ConstantPricesProduceConstantKAMA()
    {
        var kama = Indicators.KAMA(10, 2, 30);
        var prices = TestHelpers.ConstantPrices(100m, 20);

        TestHelpers.UpdatePrices(kama, prices);

        TestHelpers.AssertReady(kama);
        TestHelpers.AssertApproximately(100m, kama.Value, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void HandlesZeroPrices()
    {
        var kama = Indicators.KAMA(10, 2, 30);
        TestHelpers.TestZeroPrices(kama, 15);

        TestHelpers.AssertReady(kama);
        TestHelpers.AssertApproximately(0m, kama.Value, TestHelpers.DefaultPrecision);
    }

    [Fact]
    public void HandlesLargePrices()
    {
        var kama = Indicators.KAMA(10, 2, 30);

        // Use large prices without overflow risk
        var largePrices = new decimal[15];
        for (int i = 0; i < 15; i++)
        {
            largePrices[i] = 1_000_000_000m + i * 100_000m;
        }

        TestHelpers.UpdatePrices(kama, largePrices);

        TestHelpers.AssertReady(kama);
        Assert.True(kama.Value > 1_000_000_000m, "KAMA should handle large prices");
    }

    [Fact]
    public void EfficiencyRatioAffectsAdaptation()
    {
        var kama = Indicators.KAMA(10, 2, 30);

        // High efficiency ratio (strong trend)
        var strongTrend = TestHelpers.AscendingPrices(100m, 5m, 15);
        TestHelpers.UpdatePrices(kama, strongTrend);

        var kamaAfterStrongTrend = kama.Value;

        kama.Reset();

        // Low efficiency ratio (choppy market)
        var choppy = new List<decimal>();
        decimal price = 100m;
        for (int i = 0; i < 15; i++)
        {
            price += (i % 2 == 0 ? 1m : -1m);
            choppy.Add(price);
        }
        TestHelpers.UpdatePrices(kama, choppy.ToArray());

        var kamaAfterChoppy = kama.Value;

        // Strong trend should move KAMA more from baseline
        // This is implicit in the different final values
        Assert.True(kamaAfterStrongTrend != kamaAfterChoppy);
    }

    [Fact]
    public void DifferentPeriods()
    {
        var kama5 = Indicators.KAMA(5, 2, 30);
        var kama20 = Indicators.KAMA(20, 2, 30);

        var prices = TestHelpers.AscendingPrices(100m, 1m, 30);

        TestHelpers.UpdatePrices(kama5, prices);
        TestHelpers.UpdatePrices(kama20, prices);

        TestHelpers.AssertReady(kama5);
        TestHelpers.AssertReady(kama20);

        // Both should track the trend
        Assert.True(kama5.Value > 100m);
        Assert.True(kama20.Value > 100m);
    }

    [Fact]
    public void Responsiveness()
    {
        var kama = Indicators.KAMA(10, 2, 30);

        var ascending = TestHelpers.AscendingPrices(100m, 1m, 15);
        var descending = TestHelpers.DescendingPrices(120m, 1m, 15);

        TestHelpers.TestResponsiveness(kama, ascending, descending);
    }

    [Fact]
    public void SineWavePrices()
    {
        var kama = Indicators.KAMA(10, 2, 30);
        var prices = TestHelpers.SineWavePrices(100m, 10m, 30);

        TestHelpers.UpdatePrices(kama, prices);

        TestHelpers.AssertReady(kama);

        // KAMA should smooth the sine wave
        TestHelpers.AssertInRange(kama.Value, 85m, 115m);
    }
}
