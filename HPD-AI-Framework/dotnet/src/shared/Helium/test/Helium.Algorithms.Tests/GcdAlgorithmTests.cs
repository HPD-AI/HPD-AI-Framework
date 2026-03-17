using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;

namespace Helium.Algorithms.Tests;

public class GcdAlgorithmTests
{
    [Fact]
    public void GcdOverInteger_MatchesKnownValues()
    {
        Assert.Equal((Integer)6, Gcd.Compute<Integer>((Integer)12, (Integer)18));
        Assert.Equal((Integer)1, Gcd.Compute<Integer>((Integer)7, (Integer)13));
        Assert.Equal((Integer)5, Gcd.Compute<Integer>((Integer)0, (Integer)5));
        Assert.Equal((Integer)5, Gcd.Compute<Integer>((Integer)5, (Integer)0));
    }

    [Fact]
    public void GcdOverPolynomial_FindsCommonFactor()
    {
        // gcd((x-1)(x-2), (x-2)(x-3)) should be (x-2) (up to scalar)
        var x = Polynomial<Rational>.X;
        var one = Polynomial<Rational>.C((Rational)1);
        var p = (x - one) * (x - Polynomial<Rational>.C((Rational)2));
        var q = (x - Polynomial<Rational>.C((Rational)2)) * (x - Polynomial<Rational>.C((Rational)3));
        var g = p.Gcd(q);
        // g should be monic (x - 2)
        Assert.Equal(1, g.Degree);
        Assert.Equal(Rational.Create((Integer)1, (Integer)1), g.LeadingCoefficient);
        // g evaluated at x=2 should be 0: the constant term should be -2
        Assert.Equal(Rational.Create((Integer)(-2), (Integer)1), g[0]);
    }

    [Fact]
    public void ExtendedGcd_BezoutIdentity()
    {
        // a*s + b*t == gcd(a, b)
        Integer a = 35;
        Integer b = 15;
        var (g, s, t) = Gcd.Extended<Integer>(a, b);
        Assert.Equal(Integer.Gcd(a, b), g);
        Assert.Equal(g, a * s + b * t);
    }

    [Fact]
    public void ExtendedGcd_CorrectCoefficients()
    {
        // Specific known case: gcd(240, 46) = 2
        Integer a = 240;
        Integer b = 46;
        var (g, s, t) = Gcd.Extended<Integer>(a, b);
        Assert.Equal((Integer)2, g);
        Assert.Equal(g, a * s + b * t);
    }

    [Fact]
    public void Lcm_ViaGcd()
    {
        Integer a = 12;
        Integer b = 18;
        var result = Lcm.Compute<Integer>(a, b);
        Assert.Equal((Integer)36, result);
    }

    [Fact]
    public void Lcm_WithZero()
    {
        Assert.Equal(Integer.Zero, Lcm.Compute<Integer>((Integer)12, Integer.Zero));
        Assert.Equal(Integer.Zero, Lcm.Compute<Integer>(Integer.Zero, (Integer)12));
    }
}
