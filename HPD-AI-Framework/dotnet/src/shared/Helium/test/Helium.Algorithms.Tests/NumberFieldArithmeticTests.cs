using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;

namespace Helium.Algorithms.Tests;

public class NumberFieldArithmeticTests
{
    // --- Helpers ---

    private static readonly Polynomial<Rational> X   = Polynomial<Rational>.X;
    private static readonly Polynomial<Rational> One = Polynomial<Rational>.One;
    private static Polynomial<Rational> C(int n) => Polynomial<Rational>.C((Rational)n);

    // K = Q[x]/(x^2 - 2), α = √2
    private static NumberField K2 => new(X * X - C(2));
    // K = Q[x]/(x^2 + 1), α = i
    private static NumberField Ki => new(X * X + One);
    // K = Q[x]/(x^3 - 2), α = ∛2
    private static NumberField K3 => new(X * X * X - C(2));

    private static NumberFieldElement Elt(Polynomial<Rational> p, NumberField k) =>
        NumberFieldElement.Create(p, k);

    private static Rational R(int num, int den) =>
        Rational.Create((Integer)num, (Integer)den);

    // =========================================================================
    // NA-01 through NA-05: Norm in Q(√2)
    // =========================================================================

    [Fact]
    public void Norm_Alpha_InK2_IsMinusTwo()
    {
        // N(√2) = -2
        var alpha = NumberFieldElement.Generator(K2);
        Assert.Equal(R(-2, 1), NumberFieldArithmetic.Norm(alpha));
    }

    [Fact]
    public void Norm_OnePlusAlpha_InK2_IsMinusOne()
    {
        // N(1+√2) = (1+√2)(1-√2) = -1
        Assert.Equal(R(-1, 1), NumberFieldArithmetic.Norm(Elt(X + One, K2)));
    }

    [Fact]
    public void Norm_Constant2_InK2_IsFour()
    {
        // N(2) = 2^2 = 4 (degree-2 extension)
        Assert.Equal(R(4, 1), NumberFieldArithmetic.Norm(Elt(C(2), K2)));
    }

    [Fact]
    public void Norm_One_IsOne()
    {
        Assert.Equal(R(1, 1), NumberFieldArithmetic.Norm(Elt(One, K2)));
        Assert.Equal(R(1, 1), NumberFieldArithmetic.Norm(Elt(One, Ki)));
    }

    [Fact]
    public void Norm_IsMultiplicative()
    {
        // N(a*b) = N(a)*N(b) in K2
        var a = NumberFieldElement.Generator(K2);   // N(a) = -2
        var b = Elt(X + One, K2);                   // N(b) = -1
        // a*b = α(α+1) = α²+α = 2+α. N(2+α) = det([[2,2],[1,2]]) = 4-2=2.
        var normAB = NumberFieldArithmetic.Norm(a * b);
        var normANormB = NumberFieldArithmetic.Norm(a) * NumberFieldArithmetic.Norm(b);
        Assert.Equal(normANormB, normAB);
    }

    // =========================================================================
    // NA-06 through NA-10: Trace in Q(√2) and extensions
    // =========================================================================

    [Fact]
    public void Trace_Alpha_InK2_IsZero()
    {
        // √2 + (-√2) = 0
        Assert.Equal(Rational.Zero, NumberFieldArithmetic.Trace(NumberFieldElement.Generator(K2)));
    }

    [Fact]
    public void Trace_OnePlusAlpha_InK2_IsTwo()
    {
        // (1+√2) + (1-√2) = 2
        Assert.Equal(R(2, 1), NumberFieldArithmetic.Trace(Elt(X + One, K2)));
    }

    [Fact]
    public void Trace_Constant2_InK2_IsFour()
    {
        // Tr(2) = 2+2 = 4 (two conjugates, both = 2)
        Assert.Equal(R(4, 1), NumberFieldArithmetic.Trace(Elt(C(2), K2)));
    }

    [Fact]
    public void Trace_One_EqualsDegreeOfExtension()
    {
        // Tr(1) = n (degree)
        Assert.Equal(R(2, 1), NumberFieldArithmetic.Trace(Elt(One, K2)));
        Assert.Equal(R(3, 1), NumberFieldArithmetic.Trace(Elt(One, K3)));
    }

    [Fact]
    public void Trace_IsAdditive()
    {
        // Tr(a+b) = Tr(a) + Tr(b)
        var a = NumberFieldElement.Generator(K2);
        var b = Elt(One, K2);
        var trAplusB = NumberFieldArithmetic.Trace(a + b);
        var trA = NumberFieldArithmetic.Trace(a);
        var trB = NumberFieldArithmetic.Trace(b);
        Assert.Equal(trA + trB, trAplusB);
    }

    // =========================================================================
    // NA-11 through NA-16: CharPoly in Q(√2)
    // =========================================================================

    [Fact]
    public void CharPoly_Alpha_InK2_IsDefiningPolynomial()
    {
        // α is primitive, so charPoly = x^2 - 2
        var cp = NumberFieldArithmetic.CharPoly(NumberFieldElement.Generator(K2));
        Assert.Equal(X * X - C(2), cp);
    }

    [Fact]
    public void CharPoly_OnePlusAlpha_InK2_IsXSquaredMinus2XMinus1()
    {
        // Conjugates: 1±√2. Sum=2, product=-1. CharPoly = x^2 - 2x - 1.
        var cp = NumberFieldArithmetic.CharPoly(Elt(X + One, K2));
        // x^2 - 2x - 1 = [−1, −2, 1] in ascending-degree coefficients
        Assert.Equal(2, cp.Degree);
        Assert.Equal((Rational)1,  cp[2]);   // monic
        Assert.Equal((Rational)(-2), cp[1]); // -Trace
        Assert.Equal((Rational)(-1), cp[0]); // (-1)^2 * Norm = -1
    }

    [Fact]
    public void CharPoly_Constant2_InK2_IsXMinus2_Squared()
    {
        // CharPoly(2) = (x-2)^2 = x^2 - 4x + 4
        var cp = NumberFieldArithmetic.CharPoly(Elt(C(2), K2));
        Assert.Equal(2, cp.Degree);
        Assert.Equal((Rational)1,  cp[2]);
        Assert.Equal((Rational)(-4), cp[1]);
        Assert.Equal((Rational)4,  cp[0]);
    }

    [Fact]
    public void CharPoly_IsMonic()
    {
        var elements = new[]
        {
            NumberFieldElement.Generator(K2),
            Elt(X + One, K2),
            Elt(C(3) * X - C(1), K2),
        };
        foreach (var a in elements)
        {
            var cp = NumberFieldArithmetic.CharPoly(a);
            Assert.Equal(K2.Degree, cp.Degree);
            Assert.Equal(Rational.One, cp[K2.Degree]);
        }
    }

    [Fact]
    public void CharPoly_ConstantTermEqualsMinusNNormTimesMinusOne()
    {
        // Constant term of char poly = (-1)^n * Norm(a).
        // For n=2: const = Norm(a).
        var a = NumberFieldElement.Generator(K2);
        var cp = NumberFieldArithmetic.CharPoly(a);
        var norm = NumberFieldArithmetic.Norm(a);
        // (-1)^2 * norm = norm = -2
        Assert.Equal(norm, cp[0]);
    }

    [Fact]
    public void CharPoly_XPowerNMinus1_CoefficientIsMinusTrace()
    {
        // Coefficient of x^{n-1} = -Trace(a)
        var a = Elt(X + One, K2);
        var cp = NumberFieldArithmetic.CharPoly(a);
        var trace = NumberFieldArithmetic.Trace(a);
        Assert.Equal(-trace, cp[K2.Degree - 1]);
    }

    // =========================================================================
    // NA-17 through NA-21: MinPoly in Q(√2)
    // =========================================================================

    [Fact]
    public void MinPoly_Alpha_InK2_IsDefiningPolynomial()
    {
        // α is primitive, MinPoly = CharPoly = x^2 - 2
        var mp = NumberFieldArithmetic.MinPoly(NumberFieldElement.Generator(K2));
        Assert.Equal(X * X - C(2), mp);
    }

    [Fact]
    public void MinPoly_Constant2_InK2_IsXMinus2()
    {
        // CharPoly = (x-2)^2. MinPoly = x-2 (linear, since 2 ∈ Q).
        var mp = NumberFieldArithmetic.MinPoly(Elt(C(2), K2));
        Assert.Equal(1, mp.Degree);
        Assert.Equal((Rational)1,   mp[1]);
        Assert.Equal((Rational)(-2), mp[0]);
    }

    [Fact]
    public void MinPoly_Zero_InK2_IsX()
    {
        // CharPoly(0) = x^n. MinPoly = x.
        var mp = NumberFieldArithmetic.MinPoly(Elt(Polynomial<Rational>.Zero, K2));
        Assert.Equal(1, mp.Degree);
        Assert.Equal((Rational)1, mp[1]);
        Assert.Equal((Rational)0, mp[0]);
    }

    [Fact]
    public void MinPoly_One_InK2_IsXMinus1()
    {
        // CharPoly(1) = (x-1)^2. MinPoly = x-1.
        var mp = NumberFieldArithmetic.MinPoly(Elt(One, K2));
        Assert.Equal(1, mp.Degree);
        Assert.Equal((Rational)1,   mp[1]);
        Assert.Equal((Rational)(-1), mp[0]);
    }

    [Fact]
    public void MinPoly_OnePlusAlpha_InK2_IsCharPoly()
    {
        // 1+α is primitive, MinPoly = CharPoly = x^2 - 2x - 1
        var mp  = NumberFieldArithmetic.MinPoly(Elt(X + One, K2));
        var cp  = NumberFieldArithmetic.CharPoly(Elt(X + One, K2));
        Assert.Equal(cp, mp);
    }

    [Fact]
    public void MinPoly_IsMonic()
    {
        var a = NumberFieldElement.Generator(K2);
        var mp = NumberFieldArithmetic.MinPoly(a);
        Assert.Equal(Rational.One, mp[mp.Degree]);
    }

    [Fact]
    public void MinPoly_DegreeLeqCharPolyDegree()
    {
        // MinPoly always divides CharPoly, so deg(min) ≤ deg(char) = n.
        var elements = new[]
        {
            NumberFieldElement.Generator(K2),
            Elt(C(2), K2),
            Elt(One, K2),
            Elt(X + One, K2),
        };
        foreach (var a in elements)
        {
            var mp = NumberFieldArithmetic.MinPoly(a);
            Assert.True(mp.Degree <= K2.Degree);
        }
    }

    [Fact]
    public void MinPoly_ElementSatisfiesItsOwnMinPoly()
    {
        // If MinPoly = x^k + c_{k-1}x^{k-1} + ... + c_0,
        // then evaluating at α gives 0 in K.
        // We evaluate p(α) by computing α^k + c_{k-1}α^{k-1} + ... + c_0 in the field.
        var a = Elt(X + One, K2);
        var mp = NumberFieldArithmetic.MinPoly(a);
        // p(α) = α² - 2α - 1 in K₂. α² = 2 in K₂. So 2 - 2(1+α) - 1 = 2 - 2 - 2α - 1 = -1 - 2α... wait.
        // Actually a = 1+α. mp(a) = a^2 - 2a - 1 = (1+α)^2 - 2(1+α) - 1
        //   = 1+2α+α² - 2-2α - 1 = α² - 2 = 0 in K₂. ✓
        // Evaluate mp(a) in the field:
        var result = EvaluatePolynomialInField(mp, a, K2);
        Assert.True(result.Value.IsZero);
    }

    [Fact]
    public void MinPoly_CharPolyDividesMinPolyPower()
    {
        // CharPoly(a) is always a power of MinPoly(a) (Cayley-Hamilton).
        // For n=2 prime degree: either MinPoly = CharPoly (primitive) or MinPoly has degree 1.
        // Verify by checking CharPoly divides some power of MinPoly, or equivalently
        // that CharPoly / MinPoly has zero remainder.
        var a = Elt(C(2), K2);  // non-primitive: MinPoly = x-2, CharPoly = (x-2)^2
        var cp = NumberFieldArithmetic.CharPoly(a);
        var mp = NumberFieldArithmetic.MinPoly(a);
        var (_, rem) = cp.DivMod(mp);
        Assert.True(rem.IsZero);
    }

    // =========================================================================
    // NA-22 through NA-28: Q(i) and Q(∛2)
    // =========================================================================

    [Fact]
    public void CharPoly_Alpha_InKi_IsXSquaredPlus1()
    {
        var cp = NumberFieldArithmetic.CharPoly(NumberFieldElement.Generator(Ki));
        Assert.Equal(X * X + One, cp);
    }

    [Fact]
    public void Norm_Alpha_InKi_IsOne()
    {
        // N(i) = i·(-i) = 1
        Assert.Equal(R(1, 1), NumberFieldArithmetic.Norm(NumberFieldElement.Generator(Ki)));
    }

    [Fact]
    public void Norm_OnePlusAlpha_InKi_IsTwo()
    {
        // N(1+i) = 2
        Assert.Equal(R(2, 1), NumberFieldArithmetic.Norm(Elt(X + One, Ki)));
    }

    [Fact]
    public void Trace_Alpha_InKi_IsZero()
    {
        // i + (-i) = 0
        Assert.Equal(Rational.Zero, NumberFieldArithmetic.Trace(NumberFieldElement.Generator(Ki)));
    }

    [Fact]
    public void MinPoly_Alpha_InKi_IsXSquaredPlus1()
    {
        var mp = NumberFieldArithmetic.MinPoly(NumberFieldElement.Generator(Ki));
        Assert.Equal(X * X + One, mp);
    }

    [Fact]
    public void Norm_Alpha_InK3_IsTwo()
    {
        // N(∛2) = ∛2 · ζ∛2 · ζ²∛2 = 2
        Assert.Equal(R(2, 1), NumberFieldArithmetic.Norm(NumberFieldElement.Generator(K3)));
    }

    [Fact]
    public void Trace_Alpha_InK3_IsZero()
    {
        // ∛2 · (1 + ζ + ζ²) = 0
        Assert.Equal(Rational.Zero, NumberFieldArithmetic.Trace(NumberFieldElement.Generator(K3)));
    }

    [Fact]
    public void CharPoly_Alpha_InK3_IsXCubedMinus2()
    {
        var cp = NumberFieldArithmetic.CharPoly(NumberFieldElement.Generator(K3));
        Assert.Equal(X * X * X - C(2), cp);
    }

    // =========================================================================
    // Helper
    // =========================================================================

    // Evaluate polynomial p at element a in a number field, using field arithmetic.
    private static NumberFieldElement EvaluatePolynomialInField(
        Polynomial<Rational> p, NumberFieldElement a, NumberField k)
    {
        var result = NumberFieldElement.FromInt(0);
        // Horner's method: result = 0, iterate from highest to lowest.
        for (int i = p.Degree; i >= 0; i--)
        {
            var coeff = NumberFieldElement.Create(Polynomial<Rational>.C(p[i]), k);
            result = result * a + coeff;
        }
        return result;
    }
}
