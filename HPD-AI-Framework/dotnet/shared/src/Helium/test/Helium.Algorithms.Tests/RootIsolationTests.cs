using Helium.Algebra;
using Helium.Algorithms;
using Helium.Primitives;

namespace Helium.Algorithms.Tests;

public class RootIsolationTests
{
    [Fact]
    public void SturmSequence_ForCubic_HasExpectedLength()
    {
        // x^3 - x = x(x-1)(x+1)
        var p = SparsePolynomial<Rational>.FromCoeffs(
            Rational.Zero,
            (Rational)(-1),
            Rational.Zero,
            Rational.One);

        var sequence = RootIsolation.Sturm.Sequence(p);

        Assert.True(sequence.Count >= 3);
        Assert.Equal(p, sequence[0]);
        Assert.Equal(PolynomialCalculus.Derivative(p), sequence[1]);
    }

    [Fact]
    public void CountDistinctRealRoots_CubicWithThreeRoots()
    {
        var p = SparsePolynomial<Rational>.FromCoeffs(
            Rational.Zero,
            (Rational)(-1),
            Rational.Zero,
            Rational.One);

        var count = RootIsolation.Sturm.CountDistinctRealRoots(p);

        Assert.Equal(3, count);
    }

    [Fact]
    public void CountDistinctRealRoots_QuadraticWithNoRealRoots()
    {
        // x^2 + 1
        var p = SparsePolynomial<Rational>.FromCoeffs(
            Rational.One,
            Rational.Zero,
            Rational.One);

        var count = RootIsolation.Sturm.CountDistinctRealRoots(p);

        Assert.Equal(0, count);
    }

    [Fact]
    public void CountRootsInOpenInterval_IsolatesSingleRoot()
    {
        // x^3 - x has roots -1, 0, 1.
        var p = SparsePolynomial<Rational>.FromCoeffs(
            Rational.Zero,
            (Rational)(-1),
            Rational.Zero,
            Rational.One);

        var count = RootIsolation.Sturm.CountRootsInOpenInterval(
            p,
            Rational.Create((Integer)(-1), (Integer)2),
            Rational.Create((Integer)1, (Integer)2));

        Assert.Equal(1, count);
    }

    [Fact]
    public void CountRootsInOpenInterval_WideIntervalCountsAllRoots()
    {
        var p = SparsePolynomial<Rational>.FromCoeffs(
            Rational.Zero,
            (Rational)(-1),
            Rational.Zero,
            Rational.One);

        var count = RootIsolation.Sturm.CountRootsInOpenInterval(p, (Rational)(-2), (Rational)2);

        Assert.Equal(3, count);
    }

    [Fact]
    public void CountRootsInOpenInterval_InvalidIntervalThrows()
    {
        var p = SparsePolynomial<Rational>.FromCoeffs(Rational.One, Rational.One);

        Assert.Throws<ArgumentException>(() =>
            RootIsolation.Sturm.CountRootsInOpenInterval(p, (Rational)1, (Rational)1));
    }
}
