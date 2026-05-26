using Helium.Finance.Solvers;

namespace Helium.Finance.Tests;

public class RootFinderTests
{
    [Fact]
    public void BisectionSolvesBracketedIncreasingRoot()
    {
        var result = RootFinders.Bisection(x => x * x - 2.0, 0.0, 2.0);

        Assert.True(result.Converged);
        Assert.Equal(RootStatus.Converged, result.Status);
        AssertClose(Math.Sqrt(2.0), result.Root, 1e-12);
        Assert.True(result.FunctionEvaluations > 0);
    }

    [Fact]
    public void BrentSolvesBracketedDecreasingRoot()
    {
        var result = RootFinders.Brent(x => 2.0 - x, 0.0, 4.0);

        Assert.True(result.Converged);
        Assert.Equal(RootStatus.Converged, result.Status);
        AssertClose(2.0, result.Root, 1e-12);
    }

    [Fact]
    public void BrentFromGuessExpandsBracketLikeQuantLibSolver()
    {
        var result = RootFinders.BrentFromGuess(
            x => x * x - 2.0,
            guess: 1.0,
            step: 0.1);

        Assert.True(result.Converged);
        Assert.Equal(RootStatus.Converged, result.Status);
        AssertClose(Math.Sqrt(2.0), result.Root, 1e-12);
        Assert.True(result.Lower <= result.Root);
        Assert.True(result.Upper >= result.Root);
        Assert.True(result.FunctionEvaluations > 2);
    }

    [Fact]
    public void BrentFromGuessReportsNoBracketAfterExpansionBudget()
    {
        var result = RootFinders.BrentFromGuess(
            x => x * x + 1.0,
            guess: 0.0,
            step: 0.1,
            maxBracketExpansions: 4);

        Assert.False(result.Converged);
        Assert.Equal(RootStatus.NoBracket, result.Status);
        Assert.Equal(4, result.Iterations);
        Assert.True(result.FunctionEvaluations > 0);
    }

    [Fact]
    public void BisectionReportsNoBracket()
    {
        var result = RootFinders.Bisection(x => x * x + 1.0, -1.0, 1.0);

        Assert.False(result.Converged);
        Assert.Equal(RootStatus.NoBracket, result.Status);
    }

    [Fact]
    public void NewtonSolvesDerivativeSupportedRoot()
    {
        var result = RootFinders.Newton(x => x * x - 2.0, x => 2.0 * x, 1.0);

        Assert.True(result.Converged);
        Assert.Equal(RootStatus.Converged, result.Status);
        AssertClose(Math.Sqrt(2.0), result.Root, 1e-12);
    }

    [Fact]
    public void NewtonSafeSolvesBracketedRootWithDerivative()
    {
        var result = RootFinders.NewtonSafe(
            x => x * x - 2.0,
            x => 2.0 * x,
            lower: 0.0,
            upper: 2.0,
            guess: 1.0);

        Assert.True(result.Converged);
        Assert.Equal(RootStatus.Converged, result.Status);
        AssertClose(Math.Sqrt(2.0), result.Root, 1e-12);
        Assert.True(result.Lower <= result.Root);
        Assert.True(result.Upper >= result.Root);
    }

    [Fact]
    public void NewtonSafeFallsBackToBracketWhenDerivativeIsFlatAtGuess()
    {
        var result = RootFinders.NewtonSafe(
            x => x * x * x,
            x => 3.0 * x * x,
            lower: -1.0,
            upper: 1.0,
            guess: 0.25);

        Assert.True(result.Converged);
        Assert.Equal(RootStatus.Converged, result.Status);
        AssertClose(0.0, result.Root, 2e-12);
    }

    [Fact]
    public void NewtonReportsFlatDerivative()
    {
        var result = RootFinders.Newton(_ => 1.0, _ => 0.0, 1.0);

        Assert.False(result.Converged);
        Assert.Equal(RootStatus.FlatDerivative, result.Status);
    }

    [Fact]
    public void SolversRejectNonFiniteTolerance()
    {
        var bisection = RootFinders.Bisection(x => x, -1.0, 1.0, absoluteTolerance: double.NaN);
        var brent = RootFinders.Brent(x => x, -1.0, 1.0, absoluteTolerance: double.PositiveInfinity);
        var brentFromGuess = RootFinders.BrentFromGuess(x => x, 1.0, 0.1, absoluteTolerance: double.NaN);
        var newton = RootFinders.Newton(x => x, _ => 1.0, 1.0, absoluteTolerance: double.NaN);
        var newtonSafe = RootFinders.NewtonSafe(x => x, _ => 1.0, -1.0, 1.0, 0.0, absoluteTolerance: double.NaN);

        Assert.Equal(RootStatus.NonFiniteInput, bisection.Status);
        Assert.Equal(RootStatus.NonFiniteInput, brent.Status);
        Assert.Equal(RootStatus.NonFiniteInput, brentFromGuess.Status);
        Assert.Equal(RootStatus.NonFiniteInput, newton.Status);
        Assert.Equal(RootStatus.NonFiniteInput, newtonSafe.Status);
    }

    [Fact]
    public void SolversRejectNullDelegatesAsInputDiagnostics()
    {
        var bisection = RootFinders.Bisection(null!, -1.0, 1.0);
        var brent = RootFinders.Brent(null!, -1.0, 1.0);
        var brentFromGuess = RootFinders.BrentFromGuess(null!, 1.0, 0.1);
        var newtonFunction = RootFinders.Newton(null!, x => x, 1.0);
        var newtonDerivative = RootFinders.Newton(x => x, null!, 1.0);
        var newtonSafeFunction = RootFinders.NewtonSafe(null!, x => x, -1.0, 1.0, 0.0);
        var newtonSafeDerivative = RootFinders.NewtonSafe(x => x, null!, -1.0, 1.0, 0.0);

        Assert.Equal(RootStatus.NonFiniteInput, bisection.Status);
        Assert.Equal(RootStatus.NonFiniteInput, brent.Status);
        Assert.Equal(RootStatus.NonFiniteInput, brentFromGuess.Status);
        Assert.Equal(RootStatus.NonFiniteInput, newtonFunction.Status);
        Assert.Equal(RootStatus.NonFiniteInput, newtonDerivative.Status);
        Assert.Equal(RootStatus.NonFiniteInput, newtonSafeFunction.Status);
        Assert.Equal(RootStatus.NonFiniteInput, newtonSafeDerivative.Status);
    }

    [Fact]
    public void NewtonSafeReportsNoBracketAndInvalidGuessDiagnostics()
    {
        var noBracket = RootFinders.NewtonSafe(
            x => x * x + 1.0,
            x => 2.0 * x,
            lower: -1.0,
            upper: 1.0,
            guess: 0.0);
        var invalidGuess = RootFinders.NewtonSafe(
            x => x,
            _ => 1.0,
            lower: -1.0,
            upper: 1.0,
            guess: 2.0);

        Assert.False(noBracket.Converged);
        Assert.Equal(RootStatus.NoBracket, noBracket.Status);
        Assert.False(invalidGuess.Converged);
        Assert.Equal(RootStatus.NonFiniteInput, invalidGuess.Status);
    }

    [Fact]
    public void NewtonReportsNonFiniteCloseStepValue()
    {
        var result = RootFinders.Newton(
            x => x == 0.0 ? double.NaN : 2e-12,
            _ => 20.0,
            guess: 1e-13,
            absoluteTolerance: 1e-12);

        Assert.False(result.Converged);
        Assert.Equal(RootStatus.NonFiniteFunctionValue, result.Status);
    }

    [Fact]
    public void RootResultAllowsFailureDiagnostics()
    {
        var result = new RootResult(
            false,
            double.NaN,
            double.NaN,
            0,
            2,
            0.0,
            1.0,
            RootStatus.NoBracket);

        Assert.False(result.Converged);
        Assert.Equal(RootStatus.NoBracket, result.Status);
    }

    [Fact]
    public void RootResultRejectsInconsistentStates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RootResult(
            true, double.NaN, 0.0, 0, 1, 0.0, 1.0, RootStatus.Converged));

        Assert.Throws<ArgumentOutOfRangeException>(() => new RootResult(
            true, 0.5, double.NaN, 0, 1, 0.0, 1.0, RootStatus.Converged));

        Assert.Throws<ArgumentOutOfRangeException>(() => new RootResult(
            true, 0.5, 0.0, 0, 1, 1.0, 0.0, RootStatus.Converged));

        Assert.Throws<ArgumentOutOfRangeException>(() => new RootResult(
            true, 0.5, 0.0, 0, 1, 0.0, 1.0, RootStatus.NoBracket));

        Assert.Throws<ArgumentOutOfRangeException>(() => new RootResult(
            false, double.NaN, double.NaN, 0, 1, 0.0, 1.0, RootStatus.Converged));

        Assert.Throws<ArgumentOutOfRangeException>(() => new RootResult(
            false, double.NaN, double.NaN, -1, 1, 0.0, 1.0, RootStatus.NoBracket));

        Assert.Throws<ArgumentOutOfRangeException>(() => new RootResult(
            false, double.NaN, double.NaN, 0, -1, 0.0, 1.0, RootStatus.NoBracket));

        Assert.Throws<ArgumentOutOfRangeException>(() => new RootResult(
            false, double.NaN, double.NaN, 0, 1, 0.0, 1.0, (RootStatus)999));

        Assert.Throws<ArgumentOutOfRangeException>(() => new RootResult(
            false, 0.5, double.NaN, 0, 1, 0.0, 1.0, RootStatus.NoBracket));

        Assert.Throws<ArgumentOutOfRangeException>(() => new RootResult(
            false, double.NaN, 0.0, 0, 1, 0.0, 1.0, RootStatus.NonFiniteInput));

        Assert.Throws<ArgumentOutOfRangeException>(() => new RootResult(
            false, 0.5, 0.0, 1, 2, 0.0, 1.0, RootStatus.NonFiniteFunctionValue));
    }

    private static void AssertClose(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
}
