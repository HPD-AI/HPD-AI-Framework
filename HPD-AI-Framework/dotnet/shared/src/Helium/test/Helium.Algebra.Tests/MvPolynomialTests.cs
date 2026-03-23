using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algebra.Tests;

public class MvPolynomialTests
{
    // --- Construction ---

    [Fact]
    public void ZeroPolynomial()
    {
        var p = MvPolynomial<Integer>.Zero;
        Assert.True(p.IsZero);
    }

    [Fact]
    public void SingleVariable()
    {
        var x = MvPolynomial<Integer>.Var(0);
        Assert.False(x.IsZero);
        Assert.Equal(Integer.One, x[Monomial.Variable(0)]);
    }

    // --- Arithmetic ---

    [Fact]
    public void Addition()
    {
        var x = MvPolynomial<Integer>.Var(0);
        var y = MvPolynomial<Integer>.Var(1);
        var sum = x + y;
        Assert.Equal(Integer.One, sum[Monomial.Variable(0)]);
        Assert.Equal(Integer.One, sum[Monomial.Variable(1)]);
    }

    [Fact]
    public void MultiplicativeIdentity()
    {
        var x = MvPolynomial<Integer>.Var(0);
        Assert.Equal(x, x * MvPolynomial<Integer>.One);
    }

    [Fact]
    public void AdditiveIdentity()
    {
        var x = MvPolynomial<Integer>.Var(0);
        Assert.Equal(x, x + MvPolynomial<Integer>.Zero);
    }

    [Fact]
    public void AdditiveInverse()
    {
        var p = MvPolynomial<Integer>.Var(0) + MvPolynomial<Integer>.C((Integer)3);
        Assert.Equal(MvPolynomial<Integer>.Zero, p + (-p));
    }

    [Fact]
    public void DifferenceOfSquares()
    {
        // (x + y)(x - y) == x^2 - y^2
        var x = MvPolynomial<Integer>.Var(0);
        var y = MvPolynomial<Integer>.Var(1);
        var product = (x + y) * (x - y);
        var xSquared = x * x;
        var ySquared = y * y;
        Assert.Equal(xSquared - ySquared, product);
    }

    [Fact]
    public void SquareOfSum()
    {
        // (x + y)^2 == x^2 + 2xy + y^2
        var x = MvPolynomial<Integer>.Var(0);
        var y = MvPolynomial<Integer>.Var(1);
        var sum = x + y;
        var squared = sum * sum;

        var x2 = x * x;
        var xy = x * y;
        var y2 = y * y;
        var expected = x2 + MvPolynomial<Integer>.C((Integer)2) * xy + y2;

        Assert.Equal(expected, squared);
    }

    [Fact]
    public void Commutativity()
    {
        var x = MvPolynomial<Integer>.Var(0);
        var y = MvPolynomial<Integer>.Var(1);
        var p = x + MvPolynomial<Integer>.C((Integer)2);
        var q = y + MvPolynomial<Integer>.C((Integer)3);
        Assert.Equal(p * q, q * p);
    }

    [Fact]
    public void Distributivity()
    {
        var x = MvPolynomial<Integer>.Var(0);
        var y = MvPolynomial<Integer>.Var(1);
        var z = MvPolynomial<Integer>.Var(2);
        Assert.Equal(x * (y + z), x * y + x * z);
    }

    // --- Monomial ---

    [Fact]
    public void MonomialComparison()
    {
        var x2 = Monomial.Variable(0) + Monomial.Variable(0); // x^2, degree 2
        var xy = Monomial.Variable(0) + Monomial.Variable(1); // xy, degree 2
        var x = Monomial.Variable(0); // x, degree 1

        Assert.True(x.CompareTo(x2) < 0); // lower degree
        // Same degree monomials are comparable.
        Assert.NotEqual(0, x2.CompareTo(xy));
    }

    [Fact]
    public void MonomialEquality()
    {
        var a = Monomial.Variable(0) + Monomial.Variable(1);
        var b = Monomial.Variable(1) + Monomial.Variable(0);
        Assert.Equal(a, b); // commutative
    }
}
