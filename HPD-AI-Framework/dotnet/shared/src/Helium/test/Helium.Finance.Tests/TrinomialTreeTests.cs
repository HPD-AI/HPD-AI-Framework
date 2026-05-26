using Helium.Finance.Options;

namespace Helium.Finance.Tests;

public class TrinomialTreeTests
{
    [Fact]
    public void EuropeanCallConvergesTowardBlackScholes()
    {
        var input = new TrinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 900);

        var treePrice = TrinomialTree.Price(input);
        var closedForm = BlackScholes.Price(new BlackScholesInput(
            input.Right,
            input.Spot,
            input.Strike,
            input.TimeToExpiry,
            input.Volatility,
            input.RiskFreeRate,
            input.DividendYield));

        AssertClose(closedForm, treePrice, 0.01);
    }

    [Fact]
    public void EuropeanPutConvergesTowardBlackScholes()
    {
        var input = new TrinomialTreeInput(
            OptionRight.Put,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 105.0,
            TimeToExpiry: 0.75,
            Volatility: 0.28,
            RiskFreeRate: 0.03,
            DividendYield: 0.01,
            Steps: 1_000);

        var treePrice = TrinomialTree.Price(input);
        var closedForm = BlackScholes.Price(new BlackScholesInput(
            input.Right,
            input.Spot,
            input.Strike,
            input.TimeToExpiry,
            input.Volatility,
            input.RiskFreeRate,
            input.DividendYield));

        AssertClose(closedForm, treePrice, 0.01);
    }

    [Fact]
    public void AmericanPutIsAtLeastEuropeanPut()
    {
        var common = new TrinomialTreeInput(
            OptionRight.Put,
            ExerciseStyle.European,
            Spot: 95.0,
            Strike: 105.0,
            TimeToExpiry: 1.0,
            Volatility: 0.25,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 700);

        var european = TrinomialTree.Price(common);
        var american = TrinomialTree.Price(common with { ExerciseStyle = ExerciseStyle.American });

        Assert.True(american >= european, $"American {american:R} should be at least European {european:R}.");
    }

    [Fact]
    public void ZeroTimeReturnsIntrinsicValue()
    {
        var price = TrinomialTree.Price(new TrinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.American,
            Spot: 115.0,
            Strike: 100.0,
            TimeToExpiry: 0.0,
            Volatility: 0.30,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 100));

        AssertClose(15.0, price, 0.0);
    }

    [Fact]
    public void PositiveExpiryRequiresPositiveStepCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrinomialTree.Price(new TrinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 0)));
    }

    [Fact]
    public void InputRejectsInvalidOptionRightAndExerciseStyle()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrinomialTreeInput(
            (OptionRight)999,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 10));

        Assert.Throws<ArgumentOutOfRangeException>(() => new TrinomialTreeInput(
            OptionRight.Call,
            (ExerciseStyle)999,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 10));
    }

    [Fact]
    public void EuropeanZeroVolatilityMatchesDiscountedDeterministicPayoff()
    {
        var input = new TrinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 95.0,
            TimeToExpiry: 1.25,
            Volatility: 0.0,
            RiskFreeRate: 0.04,
            DividendYield: 0.01,
            Steps: 100);

        var terminalSpot = input.Spot * Math.Exp((input.RiskFreeRate - input.DividendYield) * input.TimeToExpiry);
        var expected = Math.Exp(-input.RiskFreeRate * input.TimeToExpiry) * Math.Max(terminalSpot - input.Strike, 0.0);
        var actual = TrinomialTree.Price(input);

        AssertClose(expected, actual, 1e-12);
    }

    [Fact]
    public void ZeroVolatilityRejectsNonfiniteDeterministicTransform()
    {
        var input = new TrinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.0,
            RiskFreeRate: 1_000.0,
            DividendYield: 0.0,
            Steps: 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => TrinomialTree.Price(input));
    }

    [Fact]
    public void StochasticTreeRejectsNonfiniteTransform()
    {
        var input = new TrinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: double.MaxValue,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => TrinomialTree.Price(input));
    }

    [Fact]
    public void StochasticTreeRejectsNonfiniteDiscountFactor()
    {
        var input = new TrinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: -1_000.0,
            DividendYield: 0.0,
            Steps: 10_000);

        Assert.Throws<ArgumentOutOfRangeException>(() => TrinomialTree.Price(input));
    }

    private static void AssertClose(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
}
