using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algebra.Tests;

public class ExtendedGcdTests
{
    // Convenience: polynomial over Q from coefficients [a0, a1, ...] (ascending degree).
    private static Polynomial<Rational> P(params int[] coeffs)
    {
        var arr = new Rational[coeffs.Length];
        for (int i = 0; i < coeffs.Length; i++) arr[i] = (Rational)coeffs[i];
        return Polynomial<Rational>.FromCoeffs(arr);
    }

    private static Polynomial<Rational> PR(params (int num, int den)[] coeffs)
    {
        var arr = new Rational[coeffs.Length];
        for (int i = 0; i < coeffs.Length; i++) arr[i] = Rational.Create((Integer)coeffs[i].num, (Integer)coeffs[i].den);
        return Polynomial<Rational>.FromCoeffs(arr);
    }

    private static readonly Polynomial<Rational> Zero = Polynomial<Rational>.Zero;
    private static readonly Polynomial<Rational> One  = Polynomial<Rational>.One;
    private static readonly Polynomial<Rational> X    = Polynomial<Rational>.X;

    // Verify the Bezout identity: u * self + v * other = gcd.
    private static void AssertBezout(Polynomial<Rational> self, Polynomial<Rational> other,
        Polynomial<Rational> gcd, Polynomial<Rational> u, Polynomial<Rational> v)
    {
        var lhs = u * self + v * other;
        Assert.Equal(gcd, lhs);
    }

    // --- EC-01: coprime polynomials ---

    [Fact]
    public void Coprime_XSquaredPlus1_And_X_GcdIsOne()
    {
        var p = X * X + One;  // x^2 + 1
        var q = X;
        var (gcd, u, v) = p.ExtendedGcd(q);
        Assert.Equal(0, gcd.Degree);
        Assert.Equal((Rational)1, gcd[0]);
        AssertBezout(p, q, gcd, u, v);
    }

    // --- EC-02: share a factor ---

    [Fact]
    public void SharedFactor_XSquaredMinus1_And_XMinus1()
    {
        var p = X * X - One;  // x^2 - 1 = (x+1)(x-1)
        var q = X - One;      // x - 1
        var (gcd, u, v) = p.ExtendedGcd(q);
        Assert.Equal(1, gcd.Degree);
        Assert.Equal(X - One, gcd);  // monic x-1
        AssertBezout(p, q, gcd, u, v);
    }

    // --- EC-03: gcd is monic even with non-unit leading coefficients ---

    [Fact]
    public void GcdIsMonic_NonUnitLeadingCoefficients()
    {
        // 2x^2 - 2 = 2(x-1)(x+1), 2x - 2 = 2(x-1). gcd = x-1 (monic).
        var p = P(-2, 0, 2);   // 2x^2 - 2
        var q = P(-2, 2);      // 2x - 2
        var (gcd, u, v) = p.ExtendedGcd(q);
        Assert.Equal(X - One, gcd);
        AssertBezout(p, q, gcd, u, v);
    }

    // --- EC-04: self is zero ---

    [Fact]
    public void SelfZero_GcdIsOther()
    {
        var other = X + One;
        var (gcd, u, v) = Zero.ExtendedGcd(other);
        Assert.Equal(other, gcd);
        AssertBezout(Zero, other, gcd, u, v);
    }

    // --- EC-05: other is zero ---

    [Fact]
    public void OtherZero_GcdIsSelf()
    {
        var self = X + One;
        var (gcd, u, v) = self.ExtendedGcd(Zero);
        Assert.Equal(self, gcd);
        AssertBezout(self, Zero, gcd, u, v);
    }

    // --- EC-06: both zero ---

    [Fact]
    public void BothZero_GcdIsZero()
    {
        var (gcd, _, _) = Zero.ExtendedGcd(Zero);
        Assert.True(gcd.IsZero);
    }

    // --- EC-07: identical inputs ---

    [Fact]
    public void IdenticalInputs_GcdIsSelf()
    {
        var p = X * X + One;  // x^2 + 1
        var (gcd, u, v) = p.ExtendedGcd(p);
        Assert.Equal(p, gcd);
        AssertBezout(p, p, gcd, u, v);
    }

    // --- EC-08: constant coprime inputs ---

    [Fact]
    public void Constants_Coprime_GcdIsOne()
    {
        var p = Polynomial<Rational>.C(Rational.Create((Integer)3, (Integer)4));  // 3/4
        var q = Polynomial<Rational>.C(Rational.Create((Integer)1, (Integer)2));  // 1/2
        var (gcd, u, v) = p.ExtendedGcd(q);
        Assert.Equal(0, gcd.Degree);
        Assert.Equal((Rational)1, gcd[0]);
        AssertBezout(p, q, gcd, u, v);
    }

    // --- EC-09: inversion case — x coprime to x^2-2 ---

    [Fact]
    public void Inversion_X_And_XSquaredMinus2_CoprimeBezout()
    {
        var f = X * X - P(2);  // x^2 - 2
        var (gcd, u, v) = X.ExtendedGcd(f);
        Assert.Equal(0, gcd.Degree);
        Assert.Equal((Rational)1, gcd[0]);
        AssertBezout(X, f, gcd, u, v);
        // u is the inverse of x mod f. Verify u*x mod f = 1.
        var product = (u * X).DivMod(f).Remainder;
        Assert.Equal(One, product);
    }

    // --- EC-10: inversion case — (x+1) coprime to x^2-2, inverse is x-1 ---

    [Fact]
    public void Inversion_XPlus1_And_XSquaredMinus2_InverseIsXMinus1()
    {
        var f = X * X - P(2);      // x^2 - 2
        var a = X + One;           // x + 1
        var (gcd, u, _) = a.ExtendedGcd(f);
        Assert.Equal(0, gcd.Degree);
        Assert.Equal((Rational)1, gcd[0]);
        // (x+1)(x-1) = x^2-1 ≡ 1 mod (x^2-2). So inverse of x+1 is x-1.
        var product = (u * a).DivMod(f).Remainder;
        Assert.Equal(One, product);
        Assert.Equal(X - One, u);
    }

    // --- EC-11: degree constraint ---

    [Fact]
    public void DegreeConstraint_GcdDegreeLeqMinInputDegrees()
    {
        // gcd(x^3+x, x^2+1) = x^2+1 since x^3+x = x(x^2+1).
        var p = X * X * X + X;     // x^3 + x
        var q = X * X + One;       // x^2 + 1
        var (gcd, u, v) = p.ExtendedGcd(q);
        Assert.Equal(2, gcd.Degree);
        Assert.Equal(q, gcd);
        AssertBezout(p, q, gcd, u, v);
    }

    // --- EC-12: gcd of irreducible and itself squared ---

    [Fact]
    public void GcdOf_P_And_PSquared_IsP()
    {
        var p = X * X + One;          // x^2 + 1 (irreducible over Q)
        var pSq = p * p;              // (x^2+1)^2
        var (gcd, u, v) = p.ExtendedGcd(pSq);
        Assert.Equal(p, gcd);
        AssertBezout(p, pSq, gcd, u, v);
    }
}
