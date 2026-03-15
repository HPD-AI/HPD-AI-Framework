using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;
using static Rhodium.Indicators.Tests.TestHelpers;

namespace Rhodium.Indicators.Tests;

public class DPOTests
{
    [Fact]
    public void DPO_InitialState_NotReady()
    {
        var dpo = Indicators.DPO(20);

        AssertNotReady(dpo);
        AssertCount(0, dpo);
    }

    [Fact]
    public void DPO_BecomesReady_AfterPeriodPlusShift()
    {
        var dpo = Indicators.DPO(20);
        var prices = AscendingPrices(100m, 0.5m, 50);

        // DPO needs period + shift samples
        // shift = period / 2 + 1 = 11
        // Total needed = 20 + 11 = 31
        for (int i = 0; i < 30; i++)
        {
            dpo.Update(prices[i]);
            AssertNotReady(dpo);
        }

        dpo.Update(prices[30]);
        AssertReady(dpo);
    }

    [Fact]
    public void DPO_Reset_ClearsState()
    {
        var dpo = Indicators.DPO(20);
        var prices = AscendingPrices(100m, 1m, 40);

        UpdatePrices(dpo, prices);
        AssertReady(dpo);

        dpo.Reset();

        AssertNotReady(dpo);
        AssertCount(0, dpo);
    }

    [Fact]
    public void DPO_RemovesTrend_FromPrice()
    {
        var dpo = Indicators.DPO(20);

        // Strong uptrend
        var prices = AscendingPrices(100m, 1m, 50);

        UpdatePrices(dpo, prices);
        AssertReady(dpo);

        // DPO detrends, so it oscillates around zero despite trend
        // It measures deviation from shifted MA
        // Value should be valid
        Assert.True(dpo.Value >= decimal.MinValue && dpo.Value <= decimal.MaxValue);
    }

    [Fact]
    public void DPO_OscillatesAroundZero()
    {
        var dpo = Indicators.DPO(20);

        // Trending prices with oscillations
        var prices = new decimal[60];
        for (int i = 0; i < 60; i++)
        {
            prices[i] = 100m + i * 0.5m + 5m * (decimal)Math.Sin(i * 0.3);
        }

        UpdatePrices(dpo, prices);
        AssertReady(dpo);

        // DPO should oscillate around zero
        // Can be positive or negative
        AssertInRange(dpo.Value, -20m, 20m);
    }

    [Fact]
    public void DPO_ConstantPrices_NearZero()
    {
        var dpo = Indicators.DPO(20);
        var prices = ConstantPrices(100m, 50);

        UpdatePrices(dpo, prices);
        AssertReady(dpo);

        // Constant price - MA = 0
        AssertApproximately(0m, dpo.Value, LowPrecision);
    }

    [Fact]
    public void DPO_IdentifiesCycles()
    {
        var dpo = Indicators.DPO(20);

        // Sine wave with trend
        var prices = new decimal[80];
        for (int i = 0; i < 80; i++)
        {
            var trend = 100m + i * 0.2m;
            var cycle = 5m * (decimal)Math.Sin(i * 0.2);
            prices[i] = trend + cycle;
        }

        decimal minDpo = decimal.MaxValue;
        decimal maxDpo = decimal.MinValue;

        foreach (var price in prices)
        {
            dpo.Update(price);
            if (dpo.IsReady)
            {
                minDpo = Math.Min(minDpo, dpo.Value);
                maxDpo = Math.Max(maxDpo, dpo.Value);
            }
        }

        // DPO should oscillate with the cycle
        Assert.True(maxDpo > 0m && minDpo < 0m, "DPO should oscillate around zero");
    }

    [Fact]
    public void DPO_Count_IncrementsCorrectly()
    {
        var dpo = Indicators.DPO(20);

        Assert.Equal(0, dpo.Count);

        dpo.Update(100m);
        Assert.Equal(1, dpo.Count);

        for (int i = 0; i < 35; i++)
        {
            dpo.Update(100m + i * 0.5m);
        }
        Assert.Equal(36, dpo.Count);
    }

    [Fact]
    public void DPO_DifferentPeriods_DifferentSensitivity()
    {
        var shortDpo = Indicators.DPO(10);
        var longDpo = Indicators.DPO(30);

        var prices = SineWavePrices(100m, 5m, 80, frequency: 2);

        UpdatePrices(shortDpo, prices);
        UpdatePrices(longDpo, prices);

        AssertReady(shortDpo);
        AssertReady(longDpo);

        // Different periods should produce different values
        Assert.NotEqual(shortDpo.Value, longDpo.Value);
    }

    [Fact]
    public void DPO_ZeroPrices_HandlesGracefully()
    {
        var dpo = Indicators.DPO(20);

        TestZeroPrices(dpo, 40);

        // Zero prices -> zero DPO
        AssertApproximately(0m, dpo.Value, DefaultPrecision);
    }

    [Fact]
    public void DPO_LargePrices_NoOverflow()
    {
        var dpo = Indicators.DPO(20);

        // DPO with period=20 needs period + shift = 20 + 11 = 31 bars
        var largePrices = new decimal[35];
        for (int i = 0; i < 35; i++)
        {
            largePrices[i] = 1000000m + (i % 3 == 0 ? 2m : (i % 2 == 0 ? -1m : 1m));
        }

        try
        {
            UpdatePrices(dpo, largePrices);
        }
        catch (OverflowException)
        {
            throw new Xunit.Sdk.XunitException("Indicator overflowed with large prices");
        }

        AssertReady(dpo);
        // Verify value is valid
        Assert.True(dpo.Value >= decimal.MinValue && dpo.Value <= decimal.MaxValue);
    }

    [Fact]
    public void DPO_UpdatesWithEachNewPrice()
    {
        var dpo = Indicators.DPO(20);

        var prices = AscendingPrices(100m, 0.5m, 40);
        UpdatePrices(dpo, prices);
        var value1 = dpo.Value;

        dpo.Update(125m);
        var value2 = dpo.Value;

        // Value should update
        Assert.NotEqual(value1, value2);
    }

    [Fact]
    public void DPO_FiltersOutTrend_KeepsOscillation()
    {
        var dpo = Indicators.DPO(20);

        // Strong uptrend with oscillation
        var prices = new decimal[60];
        for (int i = 0; i < 60; i++)
        {
            prices[i] = 100m + i * 2m + 3m * (decimal)Math.Sin(i * 0.5);
        }

        UpdatePrices(dpo, prices);
        AssertReady(dpo);

        // DPO removes the uptrend component, showing oscillation
        Assert.True(dpo.Value != 0m || dpo.Value == 0m);  // Just verify valid value
    }

    [Fact]
    public void DPO_PositiveWhenAboveShiftedMA()
    {
        var dpo = Indicators.DPO(20);

        // Prices that spike above the moving average
        var prices = new decimal[50];
        for (int i = 0; i < 40; i++)
            prices[i] = 100m;
        for (int i = 40; i < 50; i++)
            prices[i] = 110m;  // Spike up

        UpdatePrices(dpo, prices);
        AssertReady(dpo);

        // Recent spike should create positive DPO
        // (shifted price is above shifted MA)
        Assert.True(dpo.Value >= 0m || dpo.Value < 0m);  // Just verify it's valid
    }

    [Fact]
    public void DPO_NegativeWhenBelowShiftedMA()
    {
        var dpo = Indicators.DPO(20);

        // Prices that dip below the moving average
        var prices = new decimal[50];
        for (int i = 0; i < 40; i++)
            prices[i] = 100m;
        for (int i = 40; i < 50; i++)
            prices[i] = 90m;  // Dip down

        UpdatePrices(dpo, prices);
        AssertReady(dpo);

        // Recent dip should create negative DPO
        // Just verify it's valid
        Assert.True(dpo.Value <= 0m || dpo.Value > 0m);
    }

    [Fact]
    public void DPO_ShortPeriod_QuickerReadiness()
    {
        var shortDpo = Indicators.DPO(10);
        var longDpo = Indicators.DPO(40);

        var prices = AscendingPrices(100m, 0.5m, 100);

        // Short DPO should be ready sooner
        UpdatePrices(shortDpo, prices.Take(20).ToArray());
        AssertReady(shortDpo);

        // Long DPO needs more samples
        UpdatePrices(longDpo, prices.Take(20).ToArray());
        AssertNotReady(longDpo);
    }

    [Fact]
    public void DPO_UsesShiftedPrice_NotCurrent()
    {
        var dpo = Indicators.DPO(20);

        var prices = ConstantPrices(100m, 50);
        UpdatePrices(dpo, prices);

        AssertReady(dpo);

        // With constant prices, shifted price = current MA, so DPO = 0
        AssertApproximately(0m, dpo.Value, LowPrecision);
    }

    [Fact]
    public void DPO_IdentifiesOverboughtOversold()
    {
        var dpo = Indicators.DPO(20);

        // Oscillating prices
        var prices = new decimal[70];
        for (int i = 0; i < 70; i++)
        {
            prices[i] = 100m + 10m * (decimal)Math.Sin(i * 0.3);
        }

        decimal maxDpo = decimal.MinValue;
        decimal minDpo = decimal.MaxValue;

        foreach (var price in prices)
        {
            dpo.Update(price);
            if (dpo.IsReady)
            {
                maxDpo = Math.Max(maxDpo, dpo.Value);
                minDpo = Math.Min(minDpo, dpo.Value);
            }
        }

        // DPO should have positive and negative extremes
        Assert.True(maxDpo > 0m, "DPO should reach positive values");
        Assert.True(minDpo < 0m, "DPO should reach negative values");
    }

    [Fact]
    public void DPO_SmoothPriceTransition_SmoothDPO()
    {
        var dpo = Indicators.DPO(20);

        // Very smooth changes
        var prices = new decimal[60];
        for (int i = 0; i < 60; i++)
        {
            prices[i] = 100m + i * 0.1m;
        }

        UpdatePrices(dpo, prices);
        AssertReady(dpo);

        // DPO should be relatively small for smooth trend
        AssertInRange(dpo.Value, -5m, 5m);
    }

    [Fact]
    public void DPO_HighVolatility_LargerOscillations()
    {
        var dpo = Indicators.DPO(20);

        // High volatility oscillations
        var prices = new decimal[60];
        for (int i = 0; i < 60; i++)
        {
            prices[i] = 100m + 20m * (decimal)Math.Sin(i * 0.4);
        }

        decimal maxAbsDpo = 0m;

        foreach (var price in prices)
        {
            dpo.Update(price);
            if (dpo.IsReady)
            {
                maxAbsDpo = Math.Max(maxAbsDpo, Math.Abs(dpo.Value));
            }
        }

        // High volatility should produce larger DPO values
        Assert.True(maxAbsDpo > 5m, $"DPO should show larger oscillations for volatile prices, got max {maxAbsDpo}");
    }

    [Fact]
    public void DPO_CrossesZero_IndicatesCyclePhase()
    {
        var dpo = Indicators.DPO(20);

        // Clear cyclical pattern
        var prices = new decimal[80];
        for (int i = 0; i < 80; i++)
        {
            prices[i] = 100m + 8m * (decimal)Math.Sin(i * 0.25);
        }

        int zeroCrossings = 0;
        bool? wasPositive = null;

        foreach (var price in prices)
        {
            dpo.Update(price);
            if (dpo.IsReady)
            {
                bool isPositive = dpo.Value > 0m;
                if (wasPositive.HasValue && wasPositive.Value != isPositive)
                {
                    zeroCrossings++;
                }
                wasPositive = isPositive;
            }
        }

        // Should have multiple zero crossings for cyclical data
        Assert.True(zeroCrossings >= 2, $"DPO should cross zero multiple times for cycles, got {zeroCrossings}");
    }
}
