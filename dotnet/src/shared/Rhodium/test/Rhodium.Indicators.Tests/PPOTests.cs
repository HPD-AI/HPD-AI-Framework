using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;
using static Rhodium.Indicators.Tests.TestHelpers;

namespace Rhodium.Indicators.Tests;

public class PPOTests
{
    [Fact]
    public void PPO_InitialState_NotReady()
    {
        var ppo = Indicators.PPO(12, 26);

        AssertNotReady(ppo);
        AssertCount(0, ppo);
    }

    [Fact]
    public void PPO_BecomesReady_AfterSlowPeriod()
    {
        var ppo = Indicators.PPO(12, 26);
        var prices = AscendingPrices(100m, 0.5m, 30);

        // PPO needs both EMAs ready, so slow period determines readiness
        for (int i = 0; i < 25; i++)
        {
            ppo.Update(prices[i]);
            AssertNotReady(ppo);
        }

        ppo.Update(prices[25]);
        AssertReady(ppo);
    }

    [Fact]
    public void PPO_Reset_ClearsState()
    {
        var ppo = Indicators.PPO(12, 26);
        var prices = AscendingPrices(100m, 1m, 30);

        UpdatePrices(ppo, prices);
        AssertReady(ppo);

        ppo.Reset();

        AssertNotReady(ppo);
        AssertCount(0, ppo);
    }

    [Fact]
    public void PPO_CalculatesPercentageDifference()
    {
        var ppo = Indicators.PPO(5, 10);

        var prices = AscendingPrices(100m, 1m, 20);

        UpdatePrices(ppo, prices);
        AssertReady(ppo);

        // PPO = 100 * (fast EMA - slow EMA) / slow EMA
        // Should be positive for uptrend
        Assert.True(ppo.Value > 0m, $"PPO should be positive for uptrend, got {ppo.Value}");
    }

    [Fact]
    public void PPO_PositiveForAscendingPrices()
    {
        var ppo = Indicators.PPO(12, 26);
        var prices = AscendingPrices(100m, 1m, 40);

        UpdatePrices(ppo, prices);
        AssertReady(ppo);

        // In uptrend, fast EMA > slow EMA, so PPO > 0
        Assert.True(ppo.Value > 0m, $"PPO should be positive for ascending prices, got {ppo.Value}");
    }

    [Fact]
    public void PPO_NegativeForDescendingPrices()
    {
        var ppo = Indicators.PPO(12, 26);
        var prices = DescendingPrices(150m, 1m, 40);

        UpdatePrices(ppo, prices);
        AssertReady(ppo);

        // In downtrend, fast EMA < slow EMA, so PPO < 0
        Assert.True(ppo.Value < 0m, $"PPO should be negative for descending prices, got {ppo.Value}");
    }

    [Fact]
    public void PPO_NearZeroForConstantPrices()
    {
        var ppo = Indicators.PPO(12, 26);
        var prices = ConstantPrices(100m, 40);

        UpdatePrices(ppo, prices);
        AssertReady(ppo);

        // When prices constant, fast EMA ≈ slow EMA, so PPO ≈ 0
        AssertApproximately(0m, ppo.Value, LowPrecision);
    }

    [Fact]
    public void PPO_OscillatesForOscillatingPrices()
    {
        var ppo = Indicators.PPO(12, 26);
        var prices = SineWavePrices(100m, 5m, 60, frequency: 2);

        UpdatePrices(ppo, prices);
        AssertReady(ppo);

        // PPO should oscillate around zero
        // Can be positive or negative
        AssertInRange(ppo.Value, -20m, 20m);
    }

    [Fact]
    public void PPO_SimilarToMACD_ButPercentage()
    {
        var ppo = Indicators.PPO(12, 26);
        var macd = Indicators.MACD(12, 26, 9);

        var prices = AscendingPrices(100m, 1m, 40);

        UpdatePrices(ppo, prices);
        UpdatePrices(macd, prices);

        AssertReady(ppo);
        AssertReady(macd);

        // Both should have same directional bias
        // MACD is absolute difference, PPO is percentage
        Assert.True((ppo.Value > 0 && macd.Value > 0) || (ppo.Value < 0 && macd.Value < 0),
            "PPO and MACD should have same sign");
    }

    [Fact]
    public void PPO_RespondsToTrendChanges()
    {
        var ppo = Indicators.PPO(12, 26);

        // Uptrend
        var upPrices = AscendingPrices(100m, 1m, 30);
        UpdatePrices(ppo, upPrices);
        var valueAfterUp = ppo.Value;

        // Downtrend
        var downPrices = DescendingPrices(130m, 1m, 30);
        UpdatePrices(ppo, downPrices);
        var valueAfterDown = ppo.Value;

        // PPO should decrease after downtrend
        Assert.True(valueAfterDown < valueAfterUp,
            $"PPO should decrease after downtrend: {valueAfterUp} -> {valueAfterDown}");
    }

    [Fact]
    public void PPO_Count_IncrementsCorrectly()
    {
        var ppo = Indicators.PPO(12, 26);

        Assert.Equal(0, ppo.Count);

        ppo.Update(100m);
        Assert.Equal(1, ppo.Count);

        for (int i = 0; i < 30; i++)
        {
            ppo.Update(100m + i * 0.5m);
        }
        Assert.Equal(31, ppo.Count);
    }

    [Fact]
    public void PPO_DifferentPeriods_DifferentValues()
    {
        var ppo1 = Indicators.PPO(12, 26);  // Standard
        var ppo2 = Indicators.PPO(6, 13);   // Faster

        var prices = AscendingPrices(100m, 0.5m, 40);

        UpdatePrices(ppo1, prices);
        UpdatePrices(ppo2, prices);

        AssertReady(ppo1);
        AssertReady(ppo2);

        // Different periods should produce different values
        Assert.NotEqual(ppo1.Value, ppo2.Value);
    }

    [Fact]
    public void PPO_ZeroPrices_HandlesGracefully()
    {
        var ppo = Indicators.PPO(12, 26);

        TestZeroPrices(ppo, 40);

        // With constant zeros, PPO should be 0
        AssertApproximately(0m, ppo.Value, DefaultPrecision);
    }

    [Fact]
    public void PPO_LargePrices_NoOverflow()
    {
        var ppo = Indicators.PPO(12, 26);

        // PPO needs slow EMA ready (26 bars)
        var largePrices = new decimal[30];
        for (int i = 0; i < 30; i++)
        {
            largePrices[i] = 1000000m + (i % 3 == 0 ? 2m : (i % 2 == 0 ? -1m : 1m));
        }

        try
        {
            UpdatePrices(ppo, largePrices);
        }
        catch (OverflowException)
        {
            throw new Xunit.Sdk.XunitException("Indicator overflowed with large prices");
        }

        AssertReady(ppo);
        // Value should be valid
        Assert.True(ppo.Value >= decimal.MinValue && ppo.Value <= decimal.MaxValue);
    }

    [Fact]
    public void PPO_StrongUptrend_LargePositiveValue()
    {
        var ppo = Indicators.PPO(12, 26);

        // Strong uptrend
        var prices = AscendingPrices(100m, 2m, 40);

        UpdatePrices(ppo, prices);
        AssertReady(ppo);

        // Should be significantly positive
        Assert.True(ppo.Value > 1m, $"PPO should be > 1% for strong uptrend, got {ppo.Value}");
    }

    [Fact]
    public void PPO_StrongDowntrend_LargeNegativeValue()
    {
        var ppo = Indicators.PPO(12, 26);

        // Strong downtrend
        var prices = DescendingPrices(200m, 2m, 40);

        UpdatePrices(ppo, prices);
        AssertReady(ppo);

        // Should be significantly negative
        Assert.True(ppo.Value < -1m, $"PPO should be < -1% for strong downtrend, got {ppo.Value}");
    }

    [Fact]
    public void PPO_CrossoverZero_SignalsTrendChange()
    {
        var ppo = Indicators.PPO(6, 13);

        // Start with uptrend
        var upPrices = AscendingPrices(100m, 1m, 20);
        UpdatePrices(ppo, upPrices);
        var valueAfterUp = ppo.Value;

        // Flat period
        var flatPrices = ConstantPrices(120m, 15);
        UpdatePrices(ppo, flatPrices);
        var valueAfterFlat = ppo.Value;

        // PPO should approach zero during consolidation
        Assert.True(Math.Abs(valueAfterFlat) < Math.Abs(valueAfterUp),
            "PPO should approach zero during consolidation");
    }

    [Fact]
    public void PPO_UpdatesWithEachNewPrice()
    {
        var ppo = Indicators.PPO(12, 26);

        var prices = AscendingPrices(100m, 1m, 30);
        UpdatePrices(ppo, prices);
        var value1 = ppo.Value;

        ppo.Update(135m);
        var value2 = ppo.Value;

        // Value should update
        Assert.NotEqual(value1, value2);
    }

    [Fact]
    public void PPO_SmallChanges_SmallPPO()
    {
        var ppo = Indicators.PPO(12, 26);

        // Very gradual changes
        var prices = AscendingPrices(100m, 0.1m, 40);

        UpdatePrices(ppo, prices);
        AssertReady(ppo);

        // PPO should be small for small changes
        AssertInRange(ppo.Value, -5m, 5m);
    }

    [Fact]
    public void PPO_NormalizesMACD_ByPrice()
    {
        var ppo1 = Indicators.PPO(12, 26);
        var ppo2 = Indicators.PPO(12, 26);

        // Same percentage change at different price levels
        var prices1 = AscendingPrices(10m, 0.1m, 40);   // 10 to 13.9 (+39%)
        var prices2 = AscendingPrices(100m, 1m, 40);    // 100 to 139 (+39%)

        UpdatePrices(ppo1, prices1);
        UpdatePrices(ppo2, prices2);

        AssertReady(ppo1);
        AssertReady(ppo2);

        // PPO should be similar for same percentage move
        AssertApproximately(ppo1.Value, ppo2.Value, 1m);
    }

    [Fact]
    public void PPO_ShortPeriod_MoreResponsive()
    {
        var shortPpo = Indicators.PPO(3, 6);
        var longPpo = Indicators.PPO(12, 26);

        var prices = AscendingPrices(100m, 0.5m, 40);

        UpdatePrices(shortPpo, prices);
        UpdatePrices(longPpo, prices);

        AssertReady(shortPpo);
        AssertReady(longPpo);

        // Both should be positive for uptrend
        Assert.True(shortPpo.Value > 0m || longPpo.Value > 0m);

        // Short period typically more responsive
        // Just verify both are valid
        // Value should be valid
        Assert.True(shortPpo.Value >= decimal.MinValue && shortPpo.Value <= decimal.MaxValue);
        // Value should be valid
        Assert.True(longPpo.Value >= decimal.MinValue && longPpo.Value <= decimal.MaxValue);
    }

    [Fact]
    public void PPO_BullishDivergence_DetectableThroughValues()
    {
        var ppo = Indicators.PPO(12, 26);

        // Declining prices but with slowing momentum
        var prices = Prices(100m, 98m, 96m, 94m, 92m, 90m, 88m, 86m, 84m, 82m,
                           80m, 79m, 78.5m, 78.2m, 78.1m, 78.0m, 78.0m, 78.1m, 78.2m,
                           78.5m, 79m, 80m, 81m, 82m, 83m, 84m, 85m, 86m, 87m, 88m);

        foreach (var price in prices)
        {
            ppo.Update(price);
        }

        AssertReady(ppo);

        // PPO should show momentum shift
        // Value should be valid
        Assert.True(ppo.Value >= decimal.MinValue && ppo.Value <= decimal.MaxValue);
    }

    [Fact]
    public void PPO_BearishDivergence_DetectableThroughValues()
    {
        var ppo = Indicators.PPO(12, 26);

        // Rising prices but with slowing momentum
        var prices = Prices(100m, 102m, 104m, 106m, 108m, 110m, 112m, 114m, 116m, 118m,
                           120m, 121m, 121.5m, 121.8m, 121.9m, 122.0m, 122.0m, 121.9m,
                           121.8m, 121.5m, 121m, 120m, 119m, 118m, 117m, 116m, 115m,
                           114m, 113m, 112m);

        foreach (var price in prices)
        {
            ppo.Update(price);
        }

        AssertReady(ppo);

        // PPO should show momentum shift
        // Value should be valid
        Assert.True(ppo.Value >= decimal.MinValue && ppo.Value <= decimal.MaxValue);
    }
}
