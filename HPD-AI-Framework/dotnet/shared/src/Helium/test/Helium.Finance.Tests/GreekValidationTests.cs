using Helium.Finance.Options;

namespace Helium.Finance.Tests;

public class GreekValidationTests
{
    [Fact]
    public void Black76GreeksMatchCentralFiniteDifferences()
    {
        var input = new Black76Input(OptionRight.Call, 101.0, 99.0, 1.2, 0.24, 0.97);
        var greeks = Black76.PriceAndGreeks(input);
        var finiteDifference = GreekFiniteDifferences.EstimateBlack76(input);

        AssertClose(greeks.Price, finiteDifference.Price, 1e-12);
        AssertClose(finiteDifference.Delta, greeks.Delta, 1e-6);
        AssertClose(finiteDifference.Gamma, greeks.Gamma, 1e-5);
        AssertClose(finiteDifference.Vega, greeks.Vega, 1e-6);
    }

    [Fact]
    public void OptionGreeksRejectNonfiniteValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OptionGreeks(
            price: double.NaN,
            delta: 0.0,
            gamma: 0.0,
            vega: 0.0,
            theta: 0.0,
            rho: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OptionGreeks(
            price: 1.0,
            delta: 0.0,
            gamma: double.PositiveInfinity,
            vega: 0.0,
            theta: 0.0,
            rho: 0.0));
    }

    [Fact]
    public void FiniteDifferenceGreekEstimateRejectsNonfiniteValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FiniteDifferenceGreekEstimate(
            price: 1.0,
            delta: double.NaN,
            gamma: 0.0,
            vega: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FiniteDifferenceGreekEstimate(
            price: 1.0,
            delta: 0.0,
            gamma: 0.0,
            vega: double.NegativeInfinity));
    }

    [Fact]
    public void BachelierGreeksMatchCentralFiniteDifferences()
    {
        var input = new BachelierInput(OptionRight.Put, -0.50, 0.25, 0.8, 11.0, 0.99);
        var greeks = Bachelier.PriceAndGreeks(input);
        var finiteDifference = GreekFiniteDifferences.EstimateBachelier(input);

        AssertClose(greeks.Price, finiteDifference.Price, 1e-12);
        AssertClose(finiteDifference.Delta, greeks.Delta, 1e-6);
        AssertClose(finiteDifference.Gamma, greeks.Gamma, 1e-5);
        AssertClose(finiteDifference.Vega, greeks.Vega, 1e-6);
    }

    [Theory]
    [InlineData(OptionRight.Call, 0.5)]
    [InlineData(OptionRight.Put, -0.5)]
    public void Black76AtTheMoneyBoundaryDeltaUsesHalfJumpConvention(OptionRight right, double expectedSign)
    {
        var greeks = Black76.PriceAndGreeks(new Black76Input(
            right,
            Forward: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.0,
            DiscountFactor: 0.97));

        AssertClose(expectedSign * 0.97, greeks.Delta, 0.0);
        AssertClose(0.0, greeks.Gamma, 0.0);
        AssertClose(0.0, greeks.Vega, 0.0);
    }

    [Theory]
    [InlineData(OptionRight.Call, 0.5)]
    [InlineData(OptionRight.Put, -0.5)]
    public void BachelierAtTheMoneyBoundaryDeltaUsesHalfJumpConvention(OptionRight right, double expectedSign)
    {
        var greeks = Bachelier.PriceAndGreeks(new BachelierInput(
            right,
            Forward: -0.25,
            Strike: -0.25,
            TimeToExpiry: 1.0,
            NormalVolatility: 0.0,
            DiscountFactor: 0.99));

        AssertClose(expectedSign * 0.99, greeks.Delta, 0.0);
        AssertClose(0.0, greeks.Gamma, 0.0);
        AssertClose(0.0, greeks.Vega, 0.0);
    }

    [Fact]
    public void BlackScholesGreeksMatchCentralFiniteDifferences()
    {
        var input = new BlackScholesInput(OptionRight.Call, 103.0, 100.0, 0.9, 0.21, 0.04, 0.01);
        var greeks = BlackScholes.PriceAndGreeks(input);
        var finiteDifference = GreekFiniteDifferences.EstimateBlackScholes(input);

        AssertClose(greeks.Price, finiteDifference.Price, 1e-12);
        AssertClose(finiteDifference.Delta, greeks.Delta, 1e-6);
        AssertClose(finiteDifference.Gamma, greeks.Gamma, 1e-5);
        AssertClose(finiteDifference.Vega, greeks.Vega, 1e-6);
    }

    [Fact]
    public void BlackFiniteDifferencesUseOneSidedUnderlyingBumpAtZero()
    {
        var black76 = new Black76Input(OptionRight.Call, 0.0, 100.0, 1.0, 0.20, 0.97);
        var blackScholes = new BlackScholesInput(OptionRight.Call, 0.0, 100.0, 1.0, 0.20, 0.03, 0.01);

        var black76Estimate = GreekFiniteDifferences.EstimateBlack76(black76);
        var blackScholesEstimate = GreekFiniteDifferences.EstimateBlackScholes(blackScholes);

        Assert.True(double.IsFinite(black76Estimate.Delta));
        Assert.True(double.IsFinite(black76Estimate.Gamma));
        Assert.True(double.IsFinite(blackScholesEstimate.Delta));
        Assert.True(double.IsFinite(blackScholesEstimate.Gamma));
    }

    [Theory]
    [InlineData(OptionRight.Call)]
    [InlineData(OptionRight.Put)]
    public void BlackScholesRhoMatchesRiskFreeRateFiniteDifference(OptionRight right)
    {
        var input = new BlackScholesInput(right, 103.0, 100.0, 0.9, 0.21, 0.04, 0.01);
        var greeks = BlackScholes.PriceAndGreeks(input);
        const double bump = 1e-5;
        var up = BlackScholes.Price(input with { RiskFreeRate = input.RiskFreeRate + bump });
        var down = BlackScholes.Price(input with { RiskFreeRate = input.RiskFreeRate - bump });
        var finiteDifferenceRho = (up - down) / (2.0 * bump);

        AssertClose(finiteDifferenceRho, greeks.Rho, 1e-6);
    }

    [Theory]
    [InlineData(OptionRight.Call)]
    [InlineData(OptionRight.Put)]
    public void BlackScholesThetaMatchesNegativeExpiryFiniteDifference(OptionRight right)
    {
        var input = new BlackScholesInput(right, 103.0, 100.0, 0.9, 0.21, 0.04, 0.01);
        var greeks = BlackScholes.PriceAndGreeks(input);
        const double bump = 1e-5;
        var longer = BlackScholes.Price(input with { TimeToExpiry = input.TimeToExpiry + bump });
        var shorter = BlackScholes.Price(input with { TimeToExpiry = input.TimeToExpiry - bump });
        var finiteDifferenceTheta = -(longer - shorter) / (2.0 * bump);

        AssertClose(finiteDifferenceTheta, greeks.Theta, 1e-6);
    }

    [Fact]
    public void FiniteDifferenceBumpsRejectInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FiniteDifferenceBumps(double.NaN, 1e-5).Normalize());
        Assert.Throws<ArgumentOutOfRangeException>(() => new FiniteDifferenceBumps(1e-3, -1.0).Normalize());
    }

    [Fact]
    public void FiniteDifferenceBumpsDefaultStructNormalizesToDefaultPolicy()
    {
        var input = new Black76Input(OptionRight.Call, 101.0, 99.0, 1.2, 0.24, 0.97);

        var implicitDefault = GreekFiniteDifferences.EstimateBlack76(input);
        var explicitDefaultStruct = GreekFiniteDifferences.EstimateBlack76(input, default(FiniteDifferenceBumps));

        Assert.Equal(FiniteDifferenceBumps.Default, default(FiniteDifferenceBumps).Normalize());
        AssertClose(implicitDefault.Price, explicitDefaultStruct.Price, 0.0);
        AssertClose(implicitDefault.Delta, explicitDefaultStruct.Delta, 0.0);
        AssertClose(implicitDefault.Gamma, explicitDefaultStruct.Gamma, 0.0);
        AssertClose(implicitDefault.Vega, explicitDefaultStruct.Vega, 0.0);
    }

    [Fact]
    public void FiniteDifferenceBumpsPreserveExplicitValidValues()
    {
        var bumps = new FiniteDifferenceBumps(1e-2, 1e-4).Normalize();

        Assert.Equal(new FiniteDifferenceBumps(1e-2, 1e-4), bumps);
    }

    private static void AssertClose(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
}
