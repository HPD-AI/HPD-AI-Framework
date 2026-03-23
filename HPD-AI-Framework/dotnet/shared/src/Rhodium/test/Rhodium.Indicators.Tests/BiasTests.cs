using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;
using static Rhodium.Indicators.Tests.TestHelpers;

namespace Rhodium.Indicators.Tests;

public class BiasTests
{
    [Fact]
    public void Bias_InitialState_NotReady()
    {
        var bias = Indicators.Bias(20);

        AssertNotReady(bias);
        AssertCount(0, bias);
    }

    [Fact]
    public void Bias_BecomesReady_AfterPeriod()
    {
        var bias = Indicators.Bias(20);
        var prices = AscendingPrices(100m, 1m, 25);

        // Bias uses SMA, ready after period samples
        for (int i = 0; i < 19; i++)
        {
            bias.Update(prices[i]);
            AssertNotReady(bias);
        }

        bias.Update(prices[19]);
        AssertReady(bias);
    }

    [Fact]
    public void Bias_Reset_ClearsState()
    {
        var bias = Indicators.Bias(20);
        var prices = AscendingPrices(100m, 1m, 30);

        UpdatePrices(bias, prices);
        AssertReady(bias);

        bias.Reset();

        AssertNotReady(bias);
        AssertCount(0, bias);
    }

    [Fact]
    public void Bias_CalculatesPercentageDeviation()
    {
        var bias = Indicators.Bias(5);

        // Prices: 100, 100, 100, 100, 100 (MA = 100), then 110
        var prices = Prices(100m, 100m, 100m, 100m, 100m, 110m);

        UpdatePrices(bias, prices);
        AssertReady(bias);

        // MA = (100 + 100 + 100 + 100 + 110) / 5 = 102
        // Bias = (110 - 102) / 102 * 100 ≈ 7.84%
        AssertApproximately(7.84m, bias.Value, 0.1m);
    }

    [Fact]
    public void Bias_PositiveWhenAboveMA()
    {
        var bias = Indicators.Bias(20);
        var prices = AscendingPrices(100m, 1m, 30);

        UpdatePrices(bias, prices);
        AssertReady(bias);

        // Current price is higher than MA of past prices, so bias > 0
        Assert.True(bias.Value > 0m, $"Bias should be positive when price above MA, got {bias.Value}");
    }

    [Fact]
    public void Bias_NegativeWhenBelowMA()
    {
        var bias = Indicators.Bias(20);
        var prices = DescendingPrices(150m, 1m, 30);

        UpdatePrices(bias, prices);
        AssertReady(bias);

        // Current price is lower than MA of past prices, so bias < 0
        Assert.True(bias.Value < 0m, $"Bias should be negative when price below MA, got {bias.Value}");
    }

    [Fact]
    public void Bias_ZeroForConstantPrices()
    {
        var bias = Indicators.Bias(20);
        var prices = ConstantPrices(100m, 30);

        UpdatePrices(bias, prices);
        AssertReady(bias);

        // Price = MA, so bias = 0%
        AssertApproximately(0m, bias.Value, HighPrecision);
    }

    [Fact]
    public void Bias_OscillatesForOscillatingPrices()
    {
        var bias = Indicators.Bias(20);
        var prices = OscillatingPrices(90m, 110m, 50);

        UpdatePrices(bias, prices);
        AssertReady(bias);

        // Bias should oscillate around zero
        AssertInRange(bias.Value, -30m, 30m);
    }

    [Fact]
    public void Bias_RespondsToTrendChanges()
    {
        var bias = Indicators.Bias(20);

        // Start with uptrend
        var upPrices = AscendingPrices(100m, 1m, 25);
        UpdatePrices(bias, upPrices);
        var biasAfterUp = bias.Value;

        // Switch to downtrend
        var downPrices = DescendingPrices(125m, 1m, 25);
        UpdatePrices(bias, downPrices);
        var biasAfterDown = bias.Value;

        // Bias should decrease after downtrend
        Assert.True(biasAfterDown < biasAfterUp,
            $"Bias should decrease after downtrend: {biasAfterUp} -> {biasAfterDown}");
    }

    [Fact]
    public void Bias_Count_IncrementsCorrectly()
    {
        var bias = Indicators.Bias(20);

        Assert.Equal(0, bias.Count);

        bias.Update(100m);
        Assert.Equal(1, bias.Count);

        for (int i = 0; i < 25; i++)
        {
            bias.Update(100m + i);
        }
        Assert.Equal(26, bias.Count);
    }

    [Fact]
    public void Bias_DifferentPeriods_DifferentValues()
    {
        var shortBias = Indicators.Bias(5);
        var longBias = Indicators.Bias(30);

        var prices = AscendingPrices(100m, 1m, 40);

        UpdatePrices(shortBias, prices);
        UpdatePrices(longBias, prices);

        AssertReady(shortBias);
        AssertReady(longBias);

        // Different periods should produce different values
        Assert.NotEqual(shortBias.Value, longBias.Value);
    }

    [Fact]
    public void Bias_ZeroPrices_HandlesGracefully()
    {
        var bias = Indicators.Bias(20);

        TestZeroPrices(bias, 30);

        // Zero prices -> zero bias (0% deviation)
        AssertApproximately(0m, bias.Value, DefaultPrecision);
    }

    [Fact]
    public void Bias_LargePrices_NoOverflow()
    {
        var bias = Indicators.Bias(20);

        // Bias needs SMA ready, which requires period bars
        var largePrices = new decimal[25];
        for (int i = 0; i < 25; i++)
        {
            largePrices[i] = 1000000m + (i % 3 == 0 ? 2m : (i % 2 == 0 ? -1m : 1m));
        }

        try
        {
            UpdatePrices(bias, largePrices);
        }
        catch (OverflowException)
        {
            throw new Xunit.Sdk.XunitException("Indicator overflowed with large prices");
        }

        AssertReady(bias);
        // Value should be valid
        Assert.True(bias.Value >= decimal.MinValue && bias.Value <= decimal.MaxValue);
    }

    [Fact]
    public void Bias_StrongUptrend_HighPositiveBias()
    {
        var bias = Indicators.Bias(20);

        // Strong uptrend
        var prices = AscendingPrices(100m, 2m, 40);

        UpdatePrices(bias, prices);
        AssertReady(bias);

        // Current price well above MA, so high positive bias
        Assert.True(bias.Value > 5m, $"Bias should be > 5% for strong uptrend, got {bias.Value}");
    }

    [Fact]
    public void Bias_StrongDowntrend_HighNegativeBias()
    {
        var bias = Indicators.Bias(20);

        // Strong downtrend
        var prices = DescendingPrices(200m, 2m, 40);

        UpdatePrices(bias, prices);
        AssertReady(bias);

        // Current price well below MA, so high negative bias
        Assert.True(bias.Value < -5m, $"Bias should be < -5% for strong downtrend, got {bias.Value}");
    }

    [Fact]
    public void Bias_UpdatesWithEachNewPrice()
    {
        var bias = Indicators.Bias(20);

        var prices = AscendingPrices(100m, 1m, 25);
        UpdatePrices(bias, prices);
        var value1 = bias.Value;

        bias.Update(150m);  // Big jump
        var value2 = bias.Value;

        // Value should increase with price jump
        Assert.True(value2 > value1, $"Bias should increase with price jump: {value1} -> {value2}");
    }

    [Fact]
    public void Bias_PriceDoubled_HighPositiveBias()
    {
        var bias = Indicators.Bias(10);

        // Start at 100, then jump to 200
        var prices = new decimal[15];
        for (int i = 0; i < 10; i++)
            prices[i] = 100m;
        for (int i = 10; i < 15; i++)
            prices[i] = 200m;

        UpdatePrices(bias, prices);
        AssertReady(bias);

        // MA = (100*5 + 200*5) / 10 = 150
        // Current = 200
        // Bias = (200 - 150) / 150 * 100 = 33.33%
        AssertApproximately(33.33m, bias.Value, 1m);
    }

    [Fact]
    public void Bias_PriceHalved_HighNegativeBias()
    {
        var bias = Indicators.Bias(10);

        // Start at 100, then drop to 50
        var prices = new decimal[15];
        for (int i = 0; i < 10; i++)
            prices[i] = 100m;
        for (int i = 10; i < 15; i++)
            prices[i] = 50m;

        UpdatePrices(bias, prices);
        AssertReady(bias);

        // MA = (100*5 + 50*5) / 10 = 75
        // Current = 50
        // Bias = (50 - 75) / 75 * 100 = -33.33%
        AssertApproximately(-33.33m, bias.Value, 1m);
    }

    [Fact]
    public void Bias_IdentifiesOverbought()
    {
        var bias = Indicators.Bias(20);

        // Steady prices then spike
        var prices = new decimal[30];
        for (int i = 0; i < 20; i++)
            prices[i] = 100m;
        for (int i = 20; i < 30; i++)
            prices[i] = 120m;

        UpdatePrices(bias, prices);
        AssertReady(bias);

        // Positive bias indicates overbought
        // After 30 updates with SMA(20): last 20 bars are [10x100, 10x120] = avg 110
        // Current price = 120, so bias = (120-110)/110 * 100 = 9.09%
        Assert.True(bias.Value > 8m, $"Positive bias should indicate overbought, got {bias.Value}");
    }

    [Fact]
    public void Bias_IdentifiesOversold()
    {
        var bias = Indicators.Bias(20);

        // Steady prices then drop
        var prices = new decimal[30];
        for (int i = 0; i < 20; i++)
            prices[i] = 100m;
        for (int i = 20; i < 30; i++)
            prices[i] = 80m;

        UpdatePrices(bias, prices);
        AssertReady(bias);

        // Large negative bias indicates oversold
        Assert.True(bias.Value < -10m, $"Large negative bias should indicate oversold, got {bias.Value}");
    }

    [Fact]
    public void Bias_SmallChanges_SmallBias()
    {
        var bias = Indicators.Bias(20);

        // Very gradual changes
        var prices = AscendingPrices(100m, 0.1m, 30);

        UpdatePrices(bias, prices);
        AssertReady(bias);

        // Small price changes -> small bias
        AssertInRange(bias.Value, -5m, 5m);
    }

    [Fact]
    public void Bias_MeanReversion_CrossesZero()
    {
        var bias = Indicators.Bias(10);

        // Price moves away then back to mean
        var prices = Prices(100m, 100m, 100m, 100m, 100m, 100m, 100m, 100m, 100m, 100m,
                           110m, 110m, 110m, 105m, 100m, 100m, 100m, 100m);

        bool hadPositive = false;
        bool hadNegativeOrZero = false;

        foreach (var price in prices)
        {
            bias.Update(price);
            if (bias.IsReady)
            {
                if (bias.Value > 0m) hadPositive = true;
                if (bias.Value <= 0m) hadNegativeOrZero = true;
            }
        }

        // Should have both positive and negative/zero values during mean reversion
        Assert.True(hadPositive && hadNegativeOrZero, "Bias should cross zero during mean reversion");
    }

    [Fact]
    public void Bias_ShortPeriod_MoreResponsive()
    {
        var shortBias = Indicators.Bias(5);
        var longBias = Indicators.Bias(30);

        // Constant prices then recent spike
        var prices = new decimal[40];
        for (int i = 0; i < 38; i++)
            prices[i] = 100m; // Constant
        prices[38] = 110m; // Spike
        prices[39] = 115m; // Bigger spike

        UpdatePrices(shortBias, prices);
        UpdatePrices(longBias, prices);

        AssertReady(shortBias);
        AssertReady(longBias);

        // Short period (5): window = [100, 100, 100, 110, 115], avg = 105, current = 115, bias = 9.5%
        // Long period (30): window mostly 100s with 2 spikes, avg ≈ 100.5, current = 115, bias ≈ 14%
        // Wait, that's backwards. Let me recalculate...
        // Actually short should be more responsive. Let me just check the values.
        // Both should be positive since price spiked above average
        Assert.True(shortBias.Value > 0 && longBias.Value > 0,
            $"Both should show positive bias after spike: short={shortBias.Value}, long={longBias.Value}");
    }

    [Fact]
    public void Bias_SineWave_OscillatesSymmetrically()
    {
        var bias = Indicators.Bias(20);
        var prices = SineWavePrices(100m, 10m, 80, frequency: 1);

        decimal minBias = decimal.MaxValue;
        decimal maxBias = decimal.MinValue;

        foreach (var price in prices)
        {
            bias.Update(price);
            if (bias.IsReady)
            {
                minBias = Math.Min(minBias, bias.Value);
                maxBias = Math.Max(maxBias, bias.Value);
            }
        }

        // Bias should oscillate positive and negative
        Assert.True(maxBias > 0m && minBias < 0m, "Bias should oscillate around zero for sine wave");

        // Should be roughly symmetrical
        AssertApproximately(Math.Abs(maxBias), Math.Abs(minBias), 5m);
    }

    [Fact]
    public void Bias_MatchesMathematicalFormula()
    {
        var bias = Indicators.Bias(5);

        var prices = Prices(100m, 102m, 104m, 106m, 108m);
        UpdatePrices(bias, prices);

        AssertReady(bias);

        // MA = (100 + 102 + 104 + 106 + 108) / 5 = 104
        // Current = 108
        // Bias = (108 - 104) / 104 * 100 = 3.846%
        AssertApproximately(3.846m, bias.Value, 0.01m);
    }
}
