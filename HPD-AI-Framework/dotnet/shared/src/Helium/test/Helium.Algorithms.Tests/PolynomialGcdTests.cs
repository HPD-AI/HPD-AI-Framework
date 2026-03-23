using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;

namespace Helium.Algorithms.Tests;

public class PolynomialGcdTests
{
    [Fact]
    public void GcdOfCoprime_IsOne()
    {
        // gcd(x, x + 1) = 1 (coprime)
        var x = Polynomial<Rational>.X;
        var one = Polynomial<Rational>.C((Rational)1);
        var g = x.Gcd(x + one);
        // Should be a nonzero constant (monic, so 1).
        Assert.Equal(0, g.Degree);
        Assert.Equal((Rational)1, g[0]);
    }

    [Fact]
    public void GcdOfSamePolynomial()
    {
        // gcd(p, p) = p (up to scalar, result is monic)
        var x = Polynomial<Rational>.X;
        var one = Polynomial<Rational>.C((Rational)1);
        var p = x * x - one; // x^2 - 1
        var g = p.Gcd(p);
        // Should be monic version of p, which is already monic.
        Assert.Equal(p, g);
    }

    [Fact]
    public void GcdFindsCommonFactor()
    {
        // gcd((x-1)(x-2), (x-2)(x-3)) = (x-2) (monic)
        var x = Polynomial<Rational>.X;
        var p = (x - Polynomial<Rational>.C((Rational)1)) * (x - Polynomial<Rational>.C((Rational)2));
        var q = (x - Polynomial<Rational>.C((Rational)2)) * (x - Polynomial<Rational>.C((Rational)3));
        var g = p.Gcd(q);
        Assert.Equal(1, g.Degree);
        // (x - 2): leading coeff 1, constant term -2
        Assert.Equal((Rational)1, g[1]);
        Assert.Equal(Rational.Create((Integer)(-2), (Integer)1), g[0]);
    }
}
