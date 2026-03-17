using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algebra.Tests;

public class QuadraticFieldElementTests
{
    // Shorthand: Qe(a, b, d, denom) = QuadraticFieldElement.Create(...)
    private static QuadraticFieldElement Qe(int a, int b, int d, int denom) =>
        QuadraticFieldElement.Create((Integer)a, (Integer)b, (Integer)d, (Integer)denom);

    private static Rational R(int num, int den) =>
        Rational.Create((Integer)num, (Integer)den);

    private static readonly QuadraticFieldElement Zero = QuadraticFieldElement.AdditiveIdentity;
    private static readonly QuadraticFieldElement One  = QuadraticFieldElement.MultiplicativeIdentity;

    // =========================================================================
    // QF-01 through QF-06: Construction and canonical form
    // =========================================================================

    [Fact]
    public void Create_ReducesByGcd()
    {
        // gcd(|6|,|4|,8) = 2 → A=3, B=2, Denom=4
        var e = Qe(6, 4, 2, 8);
        Assert.Equal((Integer)3, e.A);
        Assert.Equal((Integer)2, e.B);
        Assert.Equal((Integer)4, e.Denom);
        Assert.Equal((Integer)2, e.D);
    }

    [Fact]
    public void Create_NegativeDenom_Flipped()
    {
        // denom=-2 < 0 → flip signs then reduce. gcd(1,1,2)=1.
        var e = Qe(1, 1, 2, -2);
        Assert.Equal((Integer)(-1), e.A);
        Assert.Equal((Integer)(-1), e.B);
        Assert.Equal((Integer)2,    e.Denom);
        Assert.Equal((Integer)2,    e.D);
    }

    [Fact]
    public void Create_BIsZero_GcdIgnoresIrrationalPart()
    {
        // gcd(4, 0, 6) = gcd(gcd(4,0), 6) = gcd(4,6) = 2 → A=2, B=0, Denom=3
        var e = Qe(4, 0, 2, 6);
        Assert.Equal((Integer)2, e.A);
        Assert.Equal((Integer)0, e.B);
        Assert.Equal((Integer)3, e.Denom);
    }

    [Fact]
    public void Create_AllZeroNumerators_DenomBecomesOne()
    {
        // gcd(0,0,5) = 5 → A=0, B=0, Denom=1
        var e = Qe(0, 0, 2, 5);
        Assert.Equal((Integer)0, e.A);
        Assert.Equal((Integer)0, e.B);
        Assert.Equal((Integer)1, e.Denom);
    }

    [Fact]
    public void SqrtD_HasCorrectComponents()
    {
        var s = QuadraticFieldElement.SqrtD((Integer)2);
        Assert.Equal((Integer)0, s.A);
        Assert.Equal((Integer)1, s.B);
        Assert.Equal((Integer)2, s.D);
        Assert.Equal((Integer)1, s.Denom);
    }

    [Fact]
    public void Rational_Factory_HasZeroB()
    {
        var e = QuadraticFieldElement.Rational((Integer)3, (Integer)4, (Integer)2);
        Assert.Equal((Integer)3, e.A);
        Assert.Equal((Integer)0, e.B);
        Assert.Equal((Integer)2, e.D);
        Assert.Equal((Integer)4, e.Denom);
    }

    // =========================================================================
    // QF-07 through QF-14: Arithmetic in Q(√2), D=2
    // =========================================================================

    [Fact]
    public void Addition_OnePlusSqrt2_Plus_OneMinusSqrt2_IsTwo()
    {
        // (1+√2) + (1-√2) = 2
        var a = Qe(1, 1, 2, 1);
        var b = Qe(1,-1, 2, 1);
        var result = a + b;
        Assert.Equal((Integer)2, result.A);
        Assert.Equal((Integer)0, result.B);
        Assert.Equal((Integer)1, result.Denom);
    }

    [Fact]
    public void Addition_HalfPlusSqrt2_Plus_HalfPlusSqrt2_IsOnePlusSqrt2()
    {
        // (1+√2)/2 + (1+√2)/2 = 1+√2
        var a = Qe(1, 1, 2, 2);
        var result = a + a;
        Assert.Equal((Integer)1, result.A);
        Assert.Equal((Integer)1, result.B);
        Assert.Equal((Integer)1, result.Denom);
    }

    [Fact]
    public void Subtraction_OnePlusSqrt2_Minus_OneMinusSqrt2_Is2Sqrt2()
    {
        var a = Qe(1, 1, 2, 1);
        var b = Qe(1,-1, 2, 1);
        var result = a - b;
        Assert.Equal((Integer)0, result.A);
        Assert.Equal((Integer)2, result.B);
        Assert.Equal((Integer)1, result.Denom);
    }

    [Fact]
    public void Multiplication_OnePlusSqrt2_Times_OneMinusSqrt2_IsMinusOne()
    {
        // (1+√2)(1-√2) = 1-2 = -1
        var a = Qe(1, 1, 2, 1);
        var b = Qe(1,-1, 2, 1);
        var result = a * b;
        Assert.Equal((Integer)(-1), result.A);
        Assert.Equal((Integer)0,    result.B);
        Assert.Equal((Integer)1,    result.Denom);
    }

    [Fact]
    public void Multiplication_Sqrt2_Times_Sqrt2_IsTwo()
    {
        var s = QuadraticFieldElement.SqrtD((Integer)2);
        var result = s * s;
        Assert.Equal((Integer)2, result.A);
        Assert.Equal((Integer)0, result.B);
        Assert.Equal((Integer)1, result.Denom);
    }

    [Fact]
    public void Multiplication_Fractions()
    {
        // (1/2)*(1/2) = 1/4
        var half = Qe(1, 0, 2, 2);
        var result = half * half;
        Assert.Equal((Integer)1, result.A);
        Assert.Equal((Integer)0, result.B);
        Assert.Equal((Integer)4, result.Denom);
    }

    [Fact]
    public void Negation_OnePlusSqrt2_IsMinusOneMinusSqrt2()
    {
        var a = Qe(1, 1, 2, 1);
        var neg = -a;
        Assert.Equal((Integer)(-1), neg.A);
        Assert.Equal((Integer)(-1), neg.B);
        Assert.Equal((Integer)1,    neg.Denom);
    }

    [Fact]
    public void Inversion_OnePlusSqrt2_IsMinusOnePlusSqrt2()
    {
        // (1+√2)⁻¹ = √2-1 = -1+√2
        // normNum = 1-2*1 = -1. Result = new(1*1, -(1*1), 2, -1) → flip → new(-1,1,2,1).
        var a = Qe(1, 1, 2, 1);
        var inv = QuadraticFieldElement.Invert(a);
        Assert.Equal((Integer)(-1), inv.A);
        Assert.Equal((Integer)1,    inv.B);
        Assert.Equal((Integer)1,    inv.Denom);
        // Round-trip
        var product = a * inv;
        Assert.Equal((Integer)1, product.A);
        Assert.Equal((Integer)0, product.B);
        Assert.Equal((Integer)1, product.Denom);
    }

    // =========================================================================
    // QF-15 through QF-19: Inversion in Q(i), D=-1
    // =========================================================================

    [Fact]
    public void Qi_Inversion_OfI_IsMinusI()
    {
        // i = (0,1,-1,1). normNum = 0-(-1)*1 = 1. Result = (0,-1,-1,1).
        var i = Qe(0, 1, -1, 1);
        var inv = QuadraticFieldElement.Invert(i);
        Assert.Equal((Integer)0,  inv.A);
        Assert.Equal((Integer)(-1), inv.B);
        Assert.Equal((Integer)(-1), inv.D);
        Assert.Equal((Integer)1,  inv.Denom);
    }

    [Fact]
    public void Qi_Inversion_OfI_RoundTrip()
    {
        var i = Qe(0, 1, -1, 1);
        var product = i * QuadraticFieldElement.Invert(i);
        Assert.Equal((Integer)1, product.A);
        Assert.Equal((Integer)0, product.B);
    }

    [Fact]
    public void Qi_Inversion_Of_1PlusI_Is_1MinusI_Over2()
    {
        // (1+i)⁻¹ = (1-i)/2. normNum = 1-(-1)*1 = 2. Result = new(1,-(1),−1,2).
        var a = Qe(1, 1, -1, 1);
        var inv = QuadraticFieldElement.Invert(a);
        Assert.Equal((Integer)1,  inv.A);
        Assert.Equal((Integer)(-1), inv.B);
        Assert.Equal((Integer)(-1), inv.D);
        Assert.Equal((Integer)2,  inv.Denom);
    }

    [Fact]
    public void Qi_Inversion_Of_1PlusI_RoundTrip()
    {
        var a = Qe(1, 1, -1, 1);
        var product = a * QuadraticFieldElement.Invert(a);
        Assert.Equal((Integer)1, product.A);
        Assert.Equal((Integer)0, product.B);
    }

    [Fact]
    public void Invert_Zero_ReturnsZero()
    {
        // IField convention: Invert(0) = 0.
        var inv = QuadraticFieldElement.Invert(Zero);
        Assert.Equal((Integer)0, inv.A);
        Assert.Equal((Integer)0, inv.B);
    }

    [Fact]
    public void Inversion_RoundTrip_SeveralElements()
    {
        int d = 2;
        var elements = new[] { Qe(1,1,d,1), Qe(3,-2,d,1), Qe(1,1,d,3), Qe(5,0,d,7) };
        foreach (var a in elements)
        {
            var product = a * QuadraticFieldElement.Invert(a);
            Assert.Equal((Integer)1, product.A);
            Assert.Equal((Integer)0, product.B);
            Assert.Equal((Integer)1, product.Denom);
        }
    }

    [Fact]
    public void Division_OnePlusSqrt2_Divided_ByOneMinusSqrt2_IsMinusThreeMinus2Sqrt2()
    {
        // (1+√2)/(1-√2) = -3-2√2
        var a = Qe(1, 1, 2, 1);
        var b = Qe(1,-1, 2, 1);
        var result = a / b;
        Assert.Equal((Integer)(-3), result.A);
        Assert.Equal((Integer)(-2), result.B);
        Assert.Equal((Integer)1,    result.Denom);
    }

    // =========================================================================
    // QF-20 through QF-28: Norm and Trace
    // =========================================================================

    [Fact]
    public void Norm_OnePlusSqrt2_IsMinusOne()
    {
        // N(1+√2) = (1-2)/1 = -1
        var a = Qe(1, 1, 2, 1);
        Assert.Equal(R(-1, 1), a.Norm());
    }

    [Fact]
    public void Norm_Sqrt2_IsMinusTwo()
    {
        // N(√2) = -2
        Assert.Equal(R(-2, 1), QuadraticFieldElement.SqrtD((Integer)2).Norm());
    }

    [Fact]
    public void Norm_I_IsOne()
    {
        // N(i) = 0-(-1)*1 = 1
        Assert.Equal(R(1, 1), Qe(0, 1, -1, 1).Norm());
    }

    [Fact]
    public void Norm_Pythagorean_3Plus4I_Over5_IsOne()
    {
        // N((3+4i)/5) = (9-(-1)*16)/25 = 25/25 = 1
        Assert.Equal(R(1, 1), Qe(3, 4, -1, 5).Norm());
    }

    [Fact]
    public void Norm_EqualsProductWithConjugate()
    {
        // N(a) = a * Conjugate(a) — expressed as rational
        var a = Qe(1, 1, 2, 1);
        var product = a * a.Conjugate();
        // product should be a purely rational element equal to Norm(a)
        Assert.Equal((Integer)0, product.B);
        var normRat = a.Norm();
        Assert.Equal(normRat.Numerator, product.A * product.Denom.Abs());
    }

    [Fact]
    public void Norm_IsMultiplicative()
    {
        // N(a*b) = N(a)*N(b)
        var a = Qe(1, 1, 2, 1);
        var b = Qe(2, 1, 2, 1);
        var normAB = (a * b).Norm();
        var normANormB = a.Norm() * b.Norm();
        Assert.Equal(normANormB, normAB);
    }

    [Fact]
    public void Trace_OnePlusSqrt2_IsTwo()
    {
        Assert.Equal(R(2, 1), Qe(1, 1, 2, 1).Trace());
    }

    [Fact]
    public void Trace_Sqrt2_IsZero()
    {
        Assert.Equal(R(0, 1), QuadraticFieldElement.SqrtD((Integer)2).Trace());
    }

    [Fact]
    public void Trace_I_IsZero()
    {
        Assert.Equal(Rational.Zero, Qe(0, 1, -1, 1).Trace());
    }

    [Fact]
    public void Trace_RationalEmbed_IsDoubled()
    {
        // Tr(3/4) = 2*(3/4) = 3/2 in Q(√2) [degree-2 extension]
        Assert.Equal(R(3, 2), Qe(3, 0, 2, 4).Trace());
    }

    [Fact]
    public void Trace_IsAdditive()
    {
        // Tr(a+b) = Tr(a) + Tr(b)
        var a = Qe(1, 1, 2, 1);
        var b = Qe(2, 3, 2, 1);
        Assert.Equal(a.Trace() + b.Trace(), (a + b).Trace());
    }

    // =========================================================================
    // QF-29 through QF-31: Conjugate
    // =========================================================================

    [Fact]
    public void Conjugate_OnePlusSqrt2_IsOneMinusSqrt2()
    {
        var a = Qe(1, 1, 2, 1);
        var conj = a.Conjugate();
        Assert.Equal((Integer)1,  conj.A);
        Assert.Equal((Integer)(-1), conj.B);
        Assert.Equal((Integer)2,  conj.D);
        Assert.Equal((Integer)1,  conj.Denom);
    }

    [Fact]
    public void Conjugate_OfConjugate_IsIdentity()
    {
        var a = Qe(3, 5, 2, 7);
        Assert.Equal(a, a.Conjugate().Conjugate());
    }

    [Fact]
    public void Conjugate_OfRational_IsUnchanged()
    {
        var a = Qe(3, 0, 2, 1);
        Assert.Equal(a, a.Conjugate());
    }

    // =========================================================================
    // QF-32 through QF-36: Equality and compatibility
    // =========================================================================

    [Fact]
    public void Equality_SameComponents_IsEqual()
    {
        Assert.Equal(Qe(1, 1, 2, 1), Qe(1, 1, 2, 1));
    }

    [Fact]
    public void Equality_SameValueDifferentD_IsNotEqual()
    {
        // Both represent rational integer 1, but D differs.
        Assert.NotEqual(Qe(1, 0, 2, 1), Qe(1, 0, 3, 1));
    }

    [Fact]
    public void Equality_DifferentValue_IsNotEqual()
    {
        Assert.NotEqual(Qe(1, 1, 2, 1), Qe(1, 2, 2, 1));
    }

    [Fact]
    public void Compatibility_DifferentD_BothNonzeroB_Throws()
    {
        var a = Qe(1, 1, 2, 1);
        var b = Qe(1, 1, 3, 1);
        Assert.Throws<InvalidOperationException>(() => _ = a + b);
        Assert.Throws<InvalidOperationException>(() => _ = a * b);
    }

    [Fact]
    public void Compatibility_DifferentD_OneHasBZero_DoesNotThrow()
    {
        // Rational element (B=0) can participate in arithmetic with any D element — no throw.
        // The operator takes D from left, so result D = left.D = 2 here.
        var rational = Qe(1, 0, 2, 1);  // B=0, D=2
        var b        = Qe(0, 1, 3, 1);  // D=3
        var result = rational + b;       // no throw; D taken from left = 2
        Assert.Equal((Integer)2, result.D);
        Assert.Equal((Integer)1, result.A);
        Assert.Equal((Integer)1, result.B);
    }

    [Fact]
    public void AdditiveIdentity_PlusElement_IsElement()
    {
        // Zero has D=0. Adding a+Zero: D taken from left=a.D. Zero+a: D taken from left=0.
        // The meaningful direction is a + Zero = a.
        var a = Qe(1, 1, 2, 1);
        var aPlusZero = a + Zero;
        Assert.Equal(a.A,    aPlusZero.A);
        Assert.Equal(a.B,    aPlusZero.B);
        Assert.Equal(a.Denom, aPlusZero.Denom);
        // Zero + a: D=0 from left, but A=a.A, B=a.B — value correct even if D=0.
        var zeroPlusA = Zero + a;
        Assert.Equal(a.A,    zeroPlusA.A);
        Assert.Equal(a.B,    zeroPlusA.B);
        Assert.Equal(a.Denom, zeroPlusA.Denom);
    }

    [Fact]
    public void MultiplicativeIdentity_TimesElement_IsElement()
    {
        // One has D=0. a*One: D taken from left=a.D. Value correct.
        var a = Qe(1, 1, 2, 1);
        var aTimesOne = a * One;
        Assert.Equal(a.A,    aTimesOne.A);
        Assert.Equal(a.B,    aTimesOne.B);
        Assert.Equal(a.Denom, aTimesOne.Denom);
        var oneTimesA = One * a;
        Assert.Equal(a.A,    oneTimesA.A);
        Assert.Equal(a.B,    oneTimesA.B);
        Assert.Equal(a.Denom, oneTimesA.Denom);
    }

    // =========================================================================
    // QF-39 through QF-48: Display
    // =========================================================================

    [Fact]
    public void Display_PurelyRationalInteger()
    {
        Assert.Equal("3", Qe(3, 0, 2, 1).ToString());
    }

    [Fact]
    public void Display_PurelyRationalFraction()
    {
        Assert.Equal("3/4", Qe(3, 0, 2, 4).ToString());
    }

    [Fact]
    public void Display_IrrationalOnly_BIsOne()
    {
        Assert.Equal("√2", Qe(0, 1, 2, 1).ToString());
    }

    [Fact]
    public void Display_IrrationalOnly_BIsMinusOne()
    {
        Assert.Equal("-√2", Qe(0, -1, 2, 1).ToString());
    }

    [Fact]
    public void Display_IrrationalOnly_BIsThree()
    {
        Assert.Equal("3√2", Qe(0, 3, 2, 1).ToString());
    }

    [Fact]
    public void Display_Mixed_IntegerDenom()
    {
        Assert.Equal("1 + √2", Qe(1, 1, 2, 1).ToString());
    }

    [Fact]
    public void Display_Mixed_FractionalDenom()
    {
        // (1+√2)/2 — gcd(1,1,2)=1 so stays as-is
        Assert.Equal("(1 + √2)/2", Qe(1, 1, 2, 2).ToString());
    }

    [Fact]
    public void Display_Zero()
    {
        Assert.Equal("0", Zero.ToString());
    }

    [Fact]
    public void Display_Qi_ImaginaryUnit()
    {
        // i = (0,1,-1,1). D=-1 so "√-1".
        Assert.Equal("√-1", Qe(0, 1, -1, 1).ToString());
    }

    [Fact]
    public void FromInt_Three_IsRational3()
    {
        var e = QuadraticFieldElement.FromInt(3);
        Assert.Equal((Integer)3, e.A);
        Assert.Equal((Integer)0, e.B);
        Assert.Equal((Integer)1, e.Denom);
    }

    [Fact]
    public void FromInt_Zero_IsZeroElement()
    {
        var e = QuadraticFieldElement.FromInt(0);
        Assert.Equal((Integer)0, e.A);
        Assert.Equal((Integer)0, e.B);
    }
}
