using Helium.Finance.Options;

namespace Helium.Finance.Tests;

public class BlackScholesTests
{
    [Fact]
    public void PriceMatchesEquivalentBlack76ForwardSetup()
    {
        const double spot = 100.0;
        const double strike = 95.0;
        const double time = 1.25;
        const double volatility = 0.23;
        const double rate = 0.04;
        const double dividendYield = 0.01;

        var blackScholes = BlackScholes.Price(new BlackScholesInput(
            OptionRight.Call,
            spot,
            strike,
            time,
            volatility,
            rate,
            dividendYield));

        var forward = spot * Math.Exp((rate - dividendYield) * time);
        var discountFactor = Math.Exp(-rate * time);
        var black76 = Black76.Price(new Black76Input(
            OptionRight.Call,
            forward,
            strike,
            time,
            volatility,
            discountFactor));

        AssertClose(black76, blackScholes, 1e-12);
    }

    [Fact]
    public void PutCallParityHoldsWithDividendYield()
    {
        const double spot = 100.0;
        const double strike = 97.0;
        const double time = 1.25;
        const double volatility = 0.23;
        const double rate = 0.04;
        const double dividendYield = 0.01;
        var call = BlackScholes.Price(new BlackScholesInput(
            OptionRight.Call,
            spot,
            strike,
            time,
            volatility,
            rate,
            dividendYield));
        var put = BlackScholes.Price(new BlackScholesInput(
            OptionRight.Put,
            spot,
            strike,
            time,
            volatility,
            rate,
            dividendYield));

        var discountedSpot = spot * Math.Exp(-dividendYield * time);
        var discountedStrike = strike * Math.Exp(-rate * time);

        AssertClose(discountedSpot - discountedStrike, call - put, 1e-12);
    }

    [Fact]
    public void ImpliedVolatilityRecoversInputVolatility()
    {
        var input = new BlackScholesInput(OptionRight.Put, 100.0, 105.0, 0.75, 0.31, 0.03, 0.01);
        var price = BlackScholes.Price(input);
        var result = BlackScholes.ImpliedVolatility(
            new BlackScholesInputWithoutVolatility(input.Right, input.Spot, input.Strike, input.TimeToExpiry, input.RiskFreeRate, input.DividendYield),
            price);

        Assert.True(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.Converged, result.Status);
        AssertClose(input.Volatility, result.Volatility, 1e-8);
    }

    [Fact]
    public void PriceRejectsNonfiniteTransformedForward()
    {
        var input = new BlackScholesInput(
            OptionRight.Call,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 1_000.0,
            DividendYield: 0.0);

        Assert.Throws<ArgumentOutOfRangeException>(() => BlackScholes.Price(input));
    }

    [Fact]
    public void ImpliedVolatilityRejectsNonpositiveTransformedDiscountFactor()
    {
        var input = new BlackScholesInputWithoutVolatility(
            OptionRight.Call,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            RiskFreeRate: 1_000.0,
            DividendYield: 1_000.0);

        Assert.Throws<ArgumentOutOfRangeException>(() => BlackScholes.ImpliedVolatility(input, marketPrice: 1.0));
    }

    private static void AssertClose(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
}
