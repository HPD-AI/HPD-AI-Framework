using Helium.Finance.Options;

namespace Helium.Finance.Tests;

public class BinomialTreeTests
{
    [Fact]
    public void EuropeanCallConvergesTowardBlackScholes()
    {
        var treeInput = new BinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 1_000);

        var treePrice = BinomialTree.Price(treeInput);
        var closedForm = BlackScholes.Price(new BlackScholesInput(
            treeInput.Right,
            treeInput.Spot,
            treeInput.Strike,
            treeInput.TimeToExpiry,
            treeInput.Volatility,
            treeInput.RiskFreeRate,
            treeInput.DividendYield));

        AssertClose(closedForm, treePrice, 0.01);
    }

    [Fact]
    public void EuropeanPutConvergesTowardBlackScholes()
    {
        var treeInput = new BinomialTreeInput(
            OptionRight.Put,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 105.0,
            TimeToExpiry: 0.75,
            Volatility: 0.28,
            RiskFreeRate: 0.03,
            DividendYield: 0.01,
            Steps: 1_200);

        var treePrice = BinomialTree.Price(treeInput);
        var closedForm = BlackScholes.Price(new BlackScholesInput(
            treeInput.Right,
            treeInput.Spot,
            treeInput.Strike,
            treeInput.TimeToExpiry,
            treeInput.Volatility,
            treeInput.RiskFreeRate,
            treeInput.DividendYield));

        AssertClose(closedForm, treePrice, 0.01);
    }

    [Fact]
    public void AmericanPutIsAtLeastEuropeanPut()
    {
        var common = new BinomialTreeInput(
            OptionRight.Put,
            ExerciseStyle.European,
            Spot: 95.0,
            Strike: 105.0,
            TimeToExpiry: 1.0,
            Volatility: 0.25,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 800);

        var european = BinomialTree.Price(common);
        var american = BinomialTree.Price(common with { ExerciseStyle = ExerciseStyle.American });

        Assert.True(american >= european, $"American {american:R} should be at least European {european:R}.");
    }

    [Fact]
    public void ZeroTimeReturnsIntrinsicValue()
    {
        var call = BinomialTree.Price(new BinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.American,
            Spot: 110.0,
            Strike: 100.0,
            TimeToExpiry: 0.0,
            Volatility: 0.20,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 50));

        var put = BinomialTree.Price(new BinomialTreeInput(
            OptionRight.Put,
            ExerciseStyle.European,
            Spot: 110.0,
            Strike: 100.0,
            TimeToExpiry: 0.0,
            Volatility: 0.20,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 50));

        AssertClose(10.0, call, 0.0);
        AssertClose(0.0, put, 0.0);
    }

    [Fact]
    public void PositiveExpiryRequiresPositiveStepCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BinomialTree.Price(new BinomialTreeInput(
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
        Assert.Throws<ArgumentOutOfRangeException>(() => new BinomialTreeInput(
            (OptionRight)999,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 10));

        Assert.Throws<ArgumentOutOfRangeException>(() => new BinomialTreeInput(
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
        var input = new BinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 95.0,
            TimeToExpiry: 1.25,
            Volatility: 0.0,
            RiskFreeRate: 0.04,
            DividendYield: 0.01,
            Steps: 100);

        var forwardSpot = input.Spot * Math.Exp((input.RiskFreeRate - input.DividendYield) * input.TimeToExpiry);
        var expected = Math.Exp(-input.RiskFreeRate * input.TimeToExpiry) * Math.Max(forwardSpot - input.Strike, 0.0);
        var actual = BinomialTree.Price(input);

        AssertClose(expected, actual, 1e-12);
    }

    [Fact]
    public void ZeroVolatilityRejectsNonfiniteDeterministicTransform()
    {
        var input = new BinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.0,
            RiskFreeRate: 1_000.0,
            DividendYield: 0.0,
            Steps: 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => BinomialTree.Price(input));
    }

    [Fact]
    public void StochasticTreeRejectsNonfiniteTransform()
    {
        var input = new BinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: double.MaxValue,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => BinomialTree.Price(input));
    }

    [Fact]
    public void StochasticTreeRejectsNonfiniteDiscountFactor()
    {
        var input = new BinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: -1_000.0,
            DividendYield: 0.0,
            Steps: 10_000);

        Assert.Throws<ArgumentOutOfRangeException>(() => BinomialTree.Price(input));
    }

    private static void AssertClose(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
}
