using Helium.Finance.Options;

namespace Helium.Finance.Tests;

public class ForwardDerivativeContractTests
{
    [Theory]
    [InlineData(OptionRight.Call)]
    [InlineData(OptionRight.Put)]
    public void Black76ForwardDerivativeStaysInsideNoArbitrageSlopeBounds(OptionRight right)
    {
        const double discountFactor = 0.97;
        var forwards = new[] { 0.0, 1e-8, 80.0, 100.0, 120.0 };
        var strikes = new[] { 0.0, 90.0, 100.0, 110.0 };
        var volatilities = new[] { 0.0, 0.05, 0.20, 1.00 };

        foreach (var forward in forwards)
        foreach (var strike in strikes)
        foreach (var volatility in volatilities)
        {
            var greeks = Black76.PriceAndGreeks(new Black76Input(
                right,
                forward,
                strike,
                TimeToExpiry: 1.0,
                volatility,
                discountFactor));

            AssertDeltaBounds(right, greeks.Delta, discountFactor);
        }
    }

    [Theory]
    [InlineData(OptionRight.Call)]
    [InlineData(OptionRight.Put)]
    public void BachelierForwardDerivativeStaysInsideNoArbitrageSlopeBounds(OptionRight right)
    {
        const double discountFactor = 0.99;
        var forwards = new[] { -100.0, -1e-8, 0.0, 1e-8, 100.0 };
        var strikes = new[] { -110.0, 0.0, 110.0 };
        var volatilities = new[] { 0.0, 0.05, 20.0, 100.0 };

        foreach (var forward in forwards)
        foreach (var strike in strikes)
        foreach (var volatility in volatilities)
        {
            var greeks = Bachelier.PriceAndGreeks(new BachelierInput(
                right,
                forward,
                strike,
                TimeToExpiry: 1.0,
                volatility,
                discountFactor));

            AssertDeltaBounds(right, greeks.Delta, discountFactor);
        }
    }

    [Theory]
    [InlineData(OptionRight.Call)]
    [InlineData(OptionRight.Put)]
    public void Black76PriceIsMonotoneInForward(OptionRight right)
    {
        var lower = Black76.Price(new Black76Input(right, 95.0, 100.0, 1.0, 0.20, 0.97));
        var higher = Black76.Price(new Black76Input(right, 105.0, 100.0, 1.0, 0.20, 0.97));

        AssertMonotoneByRight(right, lower, higher);
    }

    [Theory]
    [InlineData(OptionRight.Call)]
    [InlineData(OptionRight.Put)]
    public void BachelierPriceIsMonotoneInForward(OptionRight right)
    {
        var lower = Bachelier.Price(new BachelierInput(right, -1.0, 0.25, 1.0, 20.0, 0.99));
        var higher = Bachelier.Price(new BachelierInput(right, 1.0, 0.25, 1.0, 20.0, 0.99));

        AssertMonotoneByRight(right, lower, higher);
    }

    private static void AssertDeltaBounds(OptionRight right, double delta, double discountFactor)
    {
        Assert.True(double.IsFinite(delta));
        if (right == OptionRight.Call)
        {
            Assert.True(delta >= -1e-14, $"Call delta {delta:R} must be nonnegative.");
            Assert.True(delta <= discountFactor + 1e-14, $"Call delta {delta:R} must not exceed discount factor {discountFactor:R}.");
            return;
        }

        Assert.True(delta <= 1e-14, $"Put delta {delta:R} must be nonpositive.");
        Assert.True(delta >= -discountFactor - 1e-14, $"Put delta {delta:R} must not be below negative discount factor {-discountFactor:R}.");
    }

    private static void AssertMonotoneByRight(OptionRight right, double lowerForwardPrice, double higherForwardPrice)
    {
        if (right == OptionRight.Call)
        {
            Assert.True(higherForwardPrice >= lowerForwardPrice);
            return;
        }

        Assert.True(higherForwardPrice <= lowerForwardPrice);
    }
}
