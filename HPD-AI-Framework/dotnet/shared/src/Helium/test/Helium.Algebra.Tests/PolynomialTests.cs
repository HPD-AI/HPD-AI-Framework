using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algebra.Tests;

public class PolynomialTests
{
    // --- Construction ---

    [Fact]
    public void ZeroPolynomial()
    {
        var p = Polynomial<Integer>.Zero;
        Assert.True(p.IsZero);
        Assert.Equal(-1, p.Degree);
    }

    [Fact]
    public void OnePolynomial()
    {
        var p = Polynomial<Integer>.One;
        Assert.False(p.IsZero);
        Assert.Equal(0, p.Degree);
        Assert.Equal(Integer.One, p[0]);
    }

    [Fact]
    public void FromCoeffs()
    {
        // 1 + 5x + 3x^2
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)5, (Integer)3);
        Assert.Equal(2, p.Degree);
        Assert.Equal((Integer)1, p[0]);
        Assert.Equal((Integer)5, p[1]);
        Assert.Equal((Integer)3, p[2]);
    }

    [Fact]
    public void Monomial()
    {
        var p = Polynomial<Integer>.Monomial(3, (Integer)7);
        Assert.Equal(3, p.Degree);
        Assert.Equal((Integer)7, p[3]);
        Assert.Equal(Integer.Zero, p[0]);
    }

    [Fact]
    public void XAndC()
    {
        var x = Polynomial<Integer>.X;
        Assert.Equal(1, x.Degree);
        Assert.Equal(Integer.One, x[1]);

        var c = Polynomial<Integer>.C((Integer)5);
        Assert.Equal(0, c.Degree);
        Assert.Equal((Integer)5, c[0]);
    }

    // --- Ring axioms ---

    [Fact]
    public void AdditiveIdentity()
    {
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)2, (Integer)3);
        Assert.Equal(p, p + Polynomial<Integer>.Zero);
    }

    [Fact]
    public void MultiplicativeIdentity()
    {
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)2, (Integer)3);
        Assert.Equal(p, p * Polynomial<Integer>.One);
    }

    [Fact]
    public void AdditiveInverse()
    {
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)2, (Integer)3);
        Assert.Equal(Polynomial<Integer>.Zero, p + (-p));
    }

    [Fact]
    public void Commutativity()
    {
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)2);
        var q = Polynomial<Integer>.FromCoeffs((Integer)3, (Integer)4);
        Assert.Equal(p * q, q * p);
    }

    [Fact]
    public void Distributivity()
    {
        var a = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)2);
        var b = Polynomial<Integer>.FromCoeffs((Integer)3, (Integer)4);
        var c = Polynomial<Integer>.FromCoeffs((Integer)5, (Integer)6);
        Assert.Equal(a * (b + c), a * b + a * c);
    }

    // --- Arithmetic ---

    [Fact]
    public void Addition()
    {
        // (1 + x) + (2 + 3x) == 3 + 4x
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)1);
        var q = Polynomial<Integer>.FromCoeffs((Integer)2, (Integer)3);
        var sum = p + q;
        Assert.Equal((Integer)3, sum[0]);
        Assert.Equal((Integer)4, sum[1]);
    }

    [Fact]
    public void MultiplicationConvolution()
    {
        // (1 + x) * (1 - x) == 1 - x^2
        var onePlusX = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)1);
        var oneMinusX = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)(-1));
        var product = onePlusX * oneMinusX;
        Assert.Equal((Integer)1, product[0]);
        Assert.Equal(Integer.Zero, product[1]);
        Assert.Equal((Integer)(-1), product[2]);
    }

    [Fact]
    public void SquareOfBinomial()
    {
        // (x + 1)^2 == x^2 + 2x + 1
        var xPlusOne = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)1);
        var squared = xPlusOne * xPlusOne;
        Assert.Equal((Integer)1, squared[0]);
        Assert.Equal((Integer)2, squared[1]);
        Assert.Equal((Integer)1, squared[2]);
    }

    // --- Canonical form ---

    [Fact]
    public void NoZeroCoefficients()
    {
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)3);
        // Coefficient at degree 1 should be zero (not stored in support).
        Assert.Equal(Integer.Zero, p[1]);
        Assert.Equal(2, p.Degree);
    }

    [Fact]
    public void DegreeAlwaysCorrect()
    {
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)0, (Integer)5);
        Assert.Equal(3, p.Degree);
        Assert.Equal((Integer)5, p.LeadingCoefficient);
    }

    [Fact]
    public void DegreeOfProduct()
    {
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)1); // degree 1
        var q = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)0, (Integer)1); // degree 2
        Assert.Equal(3, (p * q).Degree);
    }

    // --- Division (over Rational = field) ---

    [Fact]
    public void DivisionExact()
    {
        // (x^2 - 1) / (x - 1) == (x + 1), remainder 0
        var dividend = Polynomial<Rational>.FromCoeffs((Rational)(-1), Rational.Zero, Rational.One);
        var divisor = Polynomial<Rational>.FromCoeffs((Rational)(-1), Rational.One);
        var (q, r) = dividend.DivMod(divisor);
        Assert.Equal(Rational.One, q[0]);
        Assert.Equal(Rational.One, q[1]);
        Assert.True(r.IsZero);
    }

    [Fact]
    public void DivisionWithRemainder()
    {
        // (x^2 + 1) / (x + 1): quotient = x - 1, remainder = 2
        var dividend = Polynomial<Rational>.FromCoeffs(Rational.One, Rational.Zero, Rational.One);
        var divisor = Polynomial<Rational>.FromCoeffs(Rational.One, Rational.One);
        var (q, r) = dividend.DivMod(divisor);

        // Verify: quotient * divisor + remainder == dividend
        var reconstructed = q * divisor + r;
        Assert.Equal(dividend, reconstructed);
        Assert.True(r.Degree < divisor.Degree);
    }

    [Fact]
    public void DivisionByZero()
    {
        var p = Polynomial<Rational>.FromCoeffs(Rational.One, Rational.One);
        var (q, r) = p.DivMod(Polynomial<Rational>.Zero);
        Assert.True(q.IsZero);
        Assert.True(r.IsZero);
    }

    // --- GCD ---

    [Fact]
    public void GcdOfCoprime()
    {
        // gcd(x + 1, x + 2) should be 1 (coprime)
        var a = Polynomial<Rational>.FromCoeffs(Rational.One, Rational.One);
        var b = Polynomial<Rational>.FromCoeffs((Rational)2, Rational.One);
        var g = a.Gcd(b);
        Assert.Equal(0, g.Degree); // constant
    }

    [Fact]
    public void GcdCommonFactor()
    {
        // gcd((x-1)(x-2), (x-2)(x-3)) == (x-2) up to scalar
        var xm1 = Polynomial<Rational>.FromCoeffs((Rational)(-1), Rational.One);
        var xm2 = Polynomial<Rational>.FromCoeffs((Rational)(-2), Rational.One);
        var xm3 = Polynomial<Rational>.FromCoeffs((Rational)(-3), Rational.One);
        var a = xm1 * xm2;
        var b = xm2 * xm3;
        var g = a.Gcd(b);
        // Should be monic of degree 1: (x - 2)
        Assert.Equal(1, g.Degree);
        Assert.Equal(Rational.One, g.LeadingCoefficient);
        Assert.Equal((Rational)(-2), g[0]);
    }

    // --- Equality ---

    [Fact]
    public void EqualPolynomials()
    {
        var a = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)2, (Integer)3);
        var b = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)2, (Integer)3);
        Assert.Equal(a, b);
    }

    // --- Sparse polynomial ---

    [Fact]
    public void SparsePolynomial()
    {
        // x^1000 + 1
        var p = Polynomial<Integer>.Monomial(1000, Integer.One) + Polynomial<Integer>.One;
        Assert.Equal(1000, p.Degree);
        Assert.Equal(Integer.One, p[0]);
        Assert.Equal(Integer.One, p[1000]);
        Assert.Equal(Integer.Zero, p[500]);
    }

    // --- Convolution (mutable-scratch path) ---

    [Fact]
    public void ConvolutionCancellationYieldsExactZero()
    {
        // (1 + x) * (1 - x) == 1 - x^2: the x^1 terms cancel exactly.
        // Verifies that the scratch-dict accumulation drops the zero coefficient.
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)1);
        var q = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)(-1));
        var r = p * q;
        Assert.Equal(2, r.Degree);
        Assert.Equal((Integer)1, r[0]);
        Assert.Equal(Integer.Zero, r[1]);  // must not appear in support
        Assert.Equal((Integer)(-1), r[2]);
        // Degree 1 should not be in the support (Finsupp drops zeros).
        Assert.DoesNotContain(1, r.Support);
    }

    [Fact]
    public void ConvolutionDegreeAdds()
    {
        // deg(p) + deg(q) == deg(p*q) when leading coefficients don't cancel.
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)2, (Integer)3); // deg 2
        var q = Polynomial<Integer>.FromCoeffs((Integer)4, (Integer)5, (Integer)6); // deg 2
        var r = p * q;
        Assert.Equal(4, r.Degree);
    }

    [Fact]
    public void ConvolutionLargeSparsePoly()
    {
        // (x^500 + 1) * (x^500 - 1) == x^1000 - 1
        // Only 3 terms involved; verifies no blowup on high-degree sparse inputs.
        var a = Polynomial<Integer>.Monomial(500, Integer.One) + Polynomial<Integer>.One;
        var b = Polynomial<Integer>.Monomial(500, Integer.One) - Polynomial<Integer>.One;
        var r = a * b;
        Assert.Equal(1000, r.Degree);
        Assert.Equal((Integer)(-1), r[0]);
        Assert.Equal(Integer.Zero, r[500]);
        Assert.Equal(Integer.One, r[1000]);
    }

    [Fact]
    public void ConvolutionDenseCorrectness()
    {
        // Multiply two dense degree-50 polynomials and verify via Horner evaluation.
        // p(x) = 1 + x + x^2 + ... + x^50, evaluated at x=2.
        // p(2) = 2^51 - 1 (geometric series).  p(2)^2 = (p*p)(2).
        var coeffs = new Integer[51];
        for (int i = 0; i <= 50; i++) coeffs[i] = Integer.One;
        var p = Polynomial<Integer>.FromCoeffs(coeffs);
        var p2 = p * p;
        Assert.Equal(100, p2.Degree);

        // Evaluate (p*p)(2) via Horner.
        var x = new System.Numerics.BigInteger(2);
        System.Numerics.BigInteger prod = 0;
        for (int i = p2.Degree; i >= 0; i--)
            prod = prod * x + (System.Numerics.BigInteger)p2[i];

        // Evaluate p(2)^2.
        System.Numerics.BigInteger pVal = 0;
        for (int i = p.Degree; i >= 0; i--)
            pVal = pVal * x + (System.Numerics.BigInteger)p[i];

        Assert.Equal(pVal * pVal, prod);
    }
}
