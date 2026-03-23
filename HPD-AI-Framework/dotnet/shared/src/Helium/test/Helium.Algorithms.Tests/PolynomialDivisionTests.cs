using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;

namespace Helium.Algorithms.Tests;

public class PolynomialDivisionTests
{
    [Fact]
    public void ExactDivision_QuotientTimesDivisorEqualsDividend()
    {
        // (x^2 - 1) / (x - 1) = (x + 1) exactly
        var x = Polynomial<Rational>.X;
        var one = Polynomial<Rational>.C((Rational)1);
        var dividend = x * x - one;
        var divisor = x - one;
        var (q, r) = dividend.DivMod(divisor);
        Assert.True(r.IsZero);
        Assert.Equal(dividend, q * divisor);
    }

    [Fact]
    public void DivisionWithRemainder_Identity()
    {
        // dividend == quotient * divisor + remainder
        var x = Polynomial<Rational>.X;
        var dividend = x * x * x + Polynomial<Rational>.C((Rational)2) * x + Polynomial<Rational>.C((Rational)1);
        var divisor = x * x + Polynomial<Rational>.C((Rational)1);
        var (q, r) = dividend.DivMod(divisor);
        Assert.Equal(dividend, q * divisor + r);
    }

    [Fact]
    public void RemainderDegree_LessThanDivisor()
    {
        var x = Polynomial<Rational>.X;
        var dividend = x * x * x + x + Polynomial<Rational>.C((Rational)1);
        var divisor = x * x + Polynomial<Rational>.C((Rational)1);
        var (_, r) = dividend.DivMod(divisor);
        Assert.True(r.IsZero || r.Degree < divisor.Degree);
    }
}
