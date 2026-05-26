using BenchmarkDotNet.Attributes;
using Helium.Finance.Solvers;

namespace Helium.Finance.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class SolverBenchmarks
{
    [Benchmark]
    public RootResult BisectionBracketedRoot() =>
        RootFinders.Bisection(QuadraticRoot, 0.0, 2.0);

    [Benchmark]
    public RootResult BrentBracketedRoot() =>
        RootFinders.Brent(QuadraticRoot, 0.0, 2.0);

    [Benchmark]
    public RootResult BrentFromGuessWithBracketExpansion() =>
        RootFinders.BrentFromGuess(QuadraticRoot, guess: 1.0, step: 0.1);

    [Benchmark]
    public RootResult NewtonRoot() =>
        RootFinders.Newton(QuadraticRoot, QuadraticDerivative, guess: 1.0);

    [Benchmark]
    public RootResult NewtonSafeBracketedRoot() =>
        RootFinders.NewtonSafe(QuadraticRoot, QuadraticDerivative, lower: 0.0, upper: 2.0, guess: 1.0);

    private static double QuadraticRoot(double x) => x * x - 2.0;

    private static double QuadraticDerivative(double x) => 2.0 * x;
}
