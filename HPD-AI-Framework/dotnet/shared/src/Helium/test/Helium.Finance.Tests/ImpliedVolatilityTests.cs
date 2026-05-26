using Helium.Finance.Options;

namespace Helium.Finance.Tests;

public class ImpliedVolatilityTests
{
    [Fact]
    public void Black76ImpliedVolatilityRecoversInputVolatility()
    {
        var input = new Black76Input(OptionRight.Call, 100.0, 103.0, 1.5, 0.27, 0.96);
        var price = Black76.Price(input);
        var result = Black76.ImpliedVolatility(
            new Black76InputWithoutVolatility(input.Right, input.Forward, input.Strike, input.TimeToExpiry, input.DiscountFactor),
            price);

        Assert.True(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.Converged, result.Status);
        AssertClose(input.Volatility, result.Volatility, 1e-8);
        Assert.True(result.Root.FunctionEvaluations > 0);
    }

    [Fact]
    public void ImpliedVolatilityResultRejectsInconsistentStates()
    {
        var root = new Solvers.RootResult(true, 0.25, 0.0, 1, 3, 0.0, 1.0, Solvers.RootStatus.Converged);
        var failedRoot = new Solvers.RootResult(false, double.NaN, double.NaN, 0, 2, 0.0, 1.0, Solvers.RootStatus.NoBracket);

        Assert.Throws<ArgumentOutOfRangeException>(() => new ImpliedVolatilityResult(
            converged: true,
            volatility: double.NaN,
            priceResidual: 0.0,
            iterations: 1,
            ImpliedVolatilityStatus.Converged,
            root));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImpliedVolatilityResult(
            converged: true,
            volatility: 0.25,
            priceResidual: 0.0,
            iterations: 1,
            ImpliedVolatilityStatus.NoBracket,
            root));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImpliedVolatilityResult(
            converged: false,
            volatility: double.NaN,
            priceResidual: double.NaN,
            iterations: 1,
            ImpliedVolatilityStatus.Converged,
            root));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImpliedVolatilityResult(
            converged: false,
            volatility: double.NaN,
            priceResidual: double.NaN,
            iterations: -1,
            ImpliedVolatilityStatus.NoBracket,
            root));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImpliedVolatilityResult(
            converged: false,
            volatility: 0.25,
            priceResidual: double.NaN,
            iterations: 1,
            ImpliedVolatilityStatus.NoBracket,
            failedRoot));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImpliedVolatilityResult(
            converged: false,
            volatility: double.NaN,
            priceResidual: 0.0,
            iterations: 1,
            ImpliedVolatilityStatus.NoBracket,
            failedRoot));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImpliedVolatilityResult(
            converged: true,
            volatility: 0.25,
            priceResidual: 0.0,
            iterations: 1,
            ImpliedVolatilityStatus.Converged,
            failedRoot));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImpliedVolatilityResult(
            converged: false,
            volatility: double.NaN,
            priceResidual: double.NaN,
            iterations: 1,
            ImpliedVolatilityStatus.NoBracket,
            root));
    }

    [Fact]
    public void Black76ImpliedVolatilityRejectsPriceBelowIntrinsic()
    {
        var result = Black76.ImpliedVolatility(
            new Black76InputWithoutVolatility(OptionRight.Call, 110.0, 100.0, 1.0, 0.95),
            marketPrice: 1.0);

        Assert.False(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.BelowIntrinsic, result.Status);
    }

    [Fact]
    public void Black76ImpliedVolatilityRejectsPriceAboveUpperBound()
    {
        var result = Black76.ImpliedVolatility(
            new Black76InputWithoutVolatility(OptionRight.Call, 100.0, 100.0, 1.0, 0.95),
            marketPrice: 200.0);

        Assert.False(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.AboveUpperBound, result.Status);
    }

    [Fact]
    public void Black76ImpliedVolatilityExpandsBeyondInitialVolatilityBracket()
    {
        var input = new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 7.5, 1.0);
        var price = Black76.Price(input);
        var result = Black76.ImpliedVolatility(
            new Black76InputWithoutVolatility(input.Right, input.Forward, input.Strike, input.TimeToExpiry, input.DiscountFactor),
            price,
            new ImpliedVolatilityOptions(UpperVolatility: 1.0));

        Assert.True(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.Converged, result.Status);
        AssertClose(input.Volatility, result.Volatility, 1e-8);
        Assert.True(result.Root.Upper > 1.0);
    }

    [Fact]
    public void Black76ImpliedVolatilityUsesConfiguredBracketWhenItContainsSolution()
    {
        var input = new Black76Input(OptionRight.Put, 102.0, 100.0, 2.0, 0.31, 0.97);
        var price = Black76.Price(input);
        var result = Black76.ImpliedVolatility(
            new Black76InputWithoutVolatility(input.Right, input.Forward, input.Strike, input.TimeToExpiry, input.DiscountFactor),
            price,
            new ImpliedVolatilityOptions(UpperVolatility: 1.0, MaxBracketExpansions: 1));

        Assert.True(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.Converged, result.Status);
        AssertClose(input.Volatility, result.Volatility, 1e-8);
        Assert.InRange(result.Root.Lower, 0.0, 1.0);
        Assert.InRange(result.Root.Upper, 0.0, 1.0);
    }

    [Fact]
    public void Black76ImpliedVolatilityUsesApproximationSeedForTightIterationBudget()
    {
        var input = new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 0.05, 0.99);
        var price = Black76.Price(input);
        var result = Black76.ImpliedVolatility(
            new Black76InputWithoutVolatility(input.Right, input.Forward, input.Strike, input.TimeToExpiry, input.DiscountFactor),
            price,
            new ImpliedVolatilityOptions(UpperVolatility: 1.0, PriceTolerance: 1e-10, MaxIterations: 3, MaxBracketExpansions: 1));

        Assert.True(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.Converged, result.Status);
        AssertClose(input.Volatility, result.Volatility, 1e-8);
    }

    [Fact]
    public void Black76ImpliedVolatilityRecoversDeepInTheMoneyCallThroughParityEquivalent()
    {
        var call = new Black76Input(OptionRight.Call, 250.0, 100.0, 0.75, 0.34, 0.96);
        var price = Black76.Price(call);
        var result = Black76.ImpliedVolatility(
            new Black76InputWithoutVolatility(call.Right, call.Forward, call.Strike, call.TimeToExpiry, call.DiscountFactor),
            price,
            new ImpliedVolatilityOptions(UpperVolatility: 1.0, MaxBracketExpansions: 1));

        Assert.True(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.Converged, result.Status);
        AssertClose(call.Volatility, result.Volatility, 1e-8);
        Assert.InRange(result.Root.Lower, 0.0, 1.0);
        Assert.InRange(result.Root.Upper, 0.0, 1.0);
    }

    [Fact]
    public void Black76ImpliedVolatilityRecoversDeepInTheMoneyPutThroughParityEquivalent()
    {
        var put = new Black76Input(OptionRight.Put, 80.0, 180.0, 1.25, 0.29, 0.94);
        var price = Black76.Price(put);
        var result = Black76.ImpliedVolatility(
            new Black76InputWithoutVolatility(put.Right, put.Forward, put.Strike, put.TimeToExpiry, put.DiscountFactor),
            price,
            new ImpliedVolatilityOptions(UpperVolatility: 1.0, MaxBracketExpansions: 1));

        Assert.True(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.Converged, result.Status);
        AssertClose(put.Volatility, result.Volatility, 1e-8);
        Assert.InRange(result.Root.Lower, 0.0, 1.0);
        Assert.InRange(result.Root.Upper, 0.0, 1.0);
    }

    [Fact]
    public void Black76ImpliedVolatilityReportsInvalidInputWithoutThrowing()
    {
        var result = Black76.ImpliedVolatility(
            new Black76InputWithoutVolatility((OptionRight)999, double.NaN, -1.0, -0.5, 0.0),
            marketPrice: 1.0);

        Assert.False(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.NonFiniteInput, result.Status);
    }

    [Fact]
    public void Black76ImpliedVolatilityReportsOverflowedBoundsWithoutThrowing()
    {
        var result = Black76.ImpliedVolatility(
            new Black76InputWithoutVolatility(OptionRight.Call, double.MaxValue, 100.0, 1.0, double.MaxValue),
            marketPrice: 1.0);

        Assert.False(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.NonFiniteInput, result.Status);
    }

    [Fact]
    public void Black76ImpliedVolatilityReportsInvalidSolverSettingsWithoutNormalizing()
    {
        var input = new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 0.25, 0.95);
        var result = Black76.ImpliedVolatility(
            new Black76InputWithoutVolatility(input.Right, input.Forward, input.Strike, input.TimeToExpiry, input.DiscountFactor),
            Black76.Price(input),
            new ImpliedVolatilityOptions(LowerVolatility: 1.0, UpperVolatility: 0.5));

        Assert.False(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.NonFiniteInput, result.Status);
        Assert.Equal(Solvers.RootStatus.NonFiniteInput, result.Root.Status);
    }

    [Fact]
    public void Black76ImpliedVolatilityTreatsDefaultStructOptionsAsDefaults()
    {
        var input = new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 0.25, 0.95);
        var result = Black76.ImpliedVolatility(
            new Black76InputWithoutVolatility(input.Right, input.Forward, input.Strike, input.TimeToExpiry, input.DiscountFactor),
            Black76.Price(input),
            default(ImpliedVolatilityOptions));

        Assert.True(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.Converged, result.Status);
        AssertClose(input.Volatility, result.Volatility, 1e-8);
    }

    [Fact]
    public void BachelierImpliedVolatilityRecoversInputVolatility()
    {
        var input = new BachelierInput(OptionRight.Put, -1.0, 0.5, 0.75, 12.0, 0.98);
        var price = Bachelier.Price(input);
        var result = Bachelier.ImpliedVolatility(
            new BachelierInputWithoutVolatility(input.Right, input.Forward, input.Strike, input.TimeToExpiry, input.DiscountFactor),
            price,
            new ImpliedVolatilityOptions(UpperVolatility: 100.0));

        Assert.True(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.Converged, result.Status);
        AssertClose(input.NormalVolatility, result.Volatility, 1e-8);
    }

    [Fact]
    public void BachelierImpliedVolatilityExpandsBeyondInitialVolatilityBracket()
    {
        var input = new BachelierInput(OptionRight.Call, 100.0, 100.0, 1.0, 2_500.0, 1.0);
        var price = Bachelier.Price(input);
        var result = Bachelier.ImpliedVolatility(
            new BachelierInputWithoutVolatility(input.Right, input.Forward, input.Strike, input.TimeToExpiry, input.DiscountFactor),
            price,
            new ImpliedVolatilityOptions(UpperVolatility: 100.0));

        Assert.True(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.Converged, result.Status);
        AssertClose(input.NormalVolatility, result.Volatility, 1e-7);
        Assert.True(result.Root.Upper > 100.0);
    }

    [Fact]
    public void BachelierImpliedVolatilityUsesConfiguredBracketWhenItContainsSolution()
    {
        var input = new BachelierInput(OptionRight.Call, -0.01, 0.015, 1.25, 0.42, 0.99);
        var price = Bachelier.Price(input);
        var result = Bachelier.ImpliedVolatility(
            new BachelierInputWithoutVolatility(input.Right, input.Forward, input.Strike, input.TimeToExpiry, input.DiscountFactor),
            price,
            new ImpliedVolatilityOptions(UpperVolatility: 1.0, MaxBracketExpansions: 1));

        Assert.True(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.Converged, result.Status);
        AssertClose(input.NormalVolatility, result.Volatility, 1e-8);
        Assert.InRange(result.Root.Lower, 0.0, 1.0);
        Assert.InRange(result.Root.Upper, 0.0, 1.0);
    }

    [Fact]
    public void BachelierImpliedVolatilityUsesAtmExactSeedForTightIterationBudget()
    {
        var input = new BachelierInput(OptionRight.Call, 100.0, 100.0, 1.0, 0.42, 0.99);
        var price = Bachelier.Price(input);
        var result = Bachelier.ImpliedVolatility(
            new BachelierInputWithoutVolatility(input.Right, input.Forward, input.Strike, input.TimeToExpiry, input.DiscountFactor),
            price,
            new ImpliedVolatilityOptions(UpperVolatility: 1.0, PriceTolerance: 1e-10, MaxIterations: 1, MaxBracketExpansions: 1));

        Assert.True(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.Converged, result.Status);
        AssertClose(input.NormalVolatility, result.Volatility, 1e-10);
    }

    [Fact]
    public void BachelierImpliedVolatilityUsesOffAtmExactSeedForTightIterationBudget()
    {
        var input = new BachelierInput(OptionRight.Put, 98.0, 101.0, 1.25, 0.73, 0.97);
        var price = Bachelier.Price(input);
        var result = Bachelier.ImpliedVolatility(
            new BachelierInputWithoutVolatility(input.Right, input.Forward, input.Strike, input.TimeToExpiry, input.DiscountFactor),
            price,
            new ImpliedVolatilityOptions(UpperVolatility: 2.0, PriceTolerance: 1e-10, MaxIterations: 1, MaxBracketExpansions: 1));

        Assert.True(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.Converged, result.Status);
        AssertClose(input.NormalVolatility, result.Volatility, 1e-8);
    }

    [Fact]
    public void BachelierImpliedVolatilityReportsInvalidInputWithoutThrowing()
    {
        var result = Bachelier.ImpliedVolatility(
            new BachelierInputWithoutVolatility((OptionRight)999, double.NaN, 0.5, -0.75, 0.0),
            marketPrice: 1.0);

        Assert.False(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.NonFiniteInput, result.Status);
    }

    [Fact]
    public void BachelierImpliedVolatilityReportsOverflowedIntrinsicWithoutThrowing()
    {
        var result = Bachelier.ImpliedVolatility(
            new BachelierInputWithoutVolatility(OptionRight.Call, double.MaxValue, -double.MaxValue, 1.0, 1.0),
            marketPrice: 1.0);

        Assert.False(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.NonFiniteInput, result.Status);
    }

    [Fact]
    public void BachelierImpliedVolatilityReportsInvalidSolverSettingsWithoutNormalizing()
    {
        var input = new BachelierInput(OptionRight.Call, 100.0, 100.0, 1.0, 12.0, 0.95);
        var result = Bachelier.ImpliedVolatility(
            new BachelierInputWithoutVolatility(input.Right, input.Forward, input.Strike, input.TimeToExpiry, input.DiscountFactor),
            Bachelier.Price(input),
            new ImpliedVolatilityOptions(PriceTolerance: double.NaN));

        Assert.False(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.NonFiniteInput, result.Status);
        Assert.Equal(Solvers.RootStatus.NonFiniteInput, result.Root.Status);
    }

    [Fact]
    public void BachelierImpliedVolatilityTreatsDefaultStructOptionsAsModelDefaults()
    {
        var input = new BachelierInput(OptionRight.Call, 100.0, 100.0, 1.0, 12.0, 0.95);
        var result = Bachelier.ImpliedVolatility(
            new BachelierInputWithoutVolatility(input.Right, input.Forward, input.Strike, input.TimeToExpiry, input.DiscountFactor),
            Bachelier.Price(input),
            default(ImpliedVolatilityOptions));

        Assert.True(result.Converged);
        Assert.Equal(ImpliedVolatilityStatus.Converged, result.Status);
        AssertClose(input.NormalVolatility, result.Volatility, 1e-8);
    }

    private static void AssertClose(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
}
