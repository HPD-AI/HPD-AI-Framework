using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algebra.Tests;

public class NumberFieldTests
{
    // --- Helpers ---

    private static readonly Polynomial<Rational> X   = Polynomial<Rational>.X;
    private static readonly Polynomial<Rational> One = Polynomial<Rational>.One;
    private static Polynomial<Rational> C(int n) => Polynomial<Rational>.C((Rational)n);

    // K = Q[x]/(x^2 - 2), generator = √2
    private static NumberField K2 => new(X * X - C(2));
    // K = Q[x]/(x^2 + 1), generator = i
    private static NumberField Ki => new(X * X + One);
    // K = Q[x]/(x^3 - 2), generator = ∛2
    private static NumberField K3 => new(X * X * X - C(2));

    private static NumberFieldElement Elt(Polynomial<Rational> p, NumberField k) =>
        NumberFieldElement.Create(p, k);

    // =========================================================================
    // NumberField struct
    // =========================================================================

    [Fact]
    public void NumberField_StoresDefiningPolynomial()
    {
        var f = X * X - C(2);
        var K = new NumberField(f);
        Assert.Equal(f, K.DefiningPolynomial);
    }

    [Fact]
    public void NumberField_DegreeMatchesPolynomialDegree()
    {
        Assert.Equal(2, K2.Degree);
        Assert.Equal(2, Ki.Degree);
        Assert.Equal(3, K3.Degree);
    }

    [Fact]
    public void NumberField_DefaultGeneratorNameIsAlpha()
    {
        var K = new NumberField(X * X - C(2));
        Assert.Equal("α", K.GeneratorName);
    }

    [Fact]
    public void NumberField_CustomGeneratorNameStored()
    {
        var K = new NumberField(X * X - C(2), "√2");
        Assert.Equal("√2", K.GeneratorName);
    }

    [Fact]
    public void NumberField_SamePolynomial_SameContextReference()
    {
        var f = X * X - C(2);
        var K1 = new NumberField(f);
        var K2b = new NumberField(f);
        Assert.True(ReferenceEquals(K1.Context, K2b.Context));
    }

    [Fact]
    public void NumberField_DifferentPolynomials_DifferentContextReference()
    {
        Assert.False(ReferenceEquals(K2.Context, Ki.Context));
        Assert.False(ReferenceEquals(K2.Context, K3.Context));
    }

    [Fact]
    public void NumberField_EqualityBySamePolynomial()
    {
        var f = X * X - C(2);
        Assert.Equal(new NumberField(f), new NumberField(f));
        Assert.True(new NumberField(f) == new NumberField(f));
    }

    [Fact]
    public void NumberField_InequalityByDifferentPolynomial()
    {
        Assert.NotEqual(K2, Ki);
        Assert.True(K2 != Ki);
    }

    // =========================================================================
    // NumberFieldElement — construction and reduction
    // =========================================================================

    [Fact]
    public void Generator_HasValueX()
    {
        var alpha = NumberFieldElement.Generator(K2);
        Assert.Equal(X, alpha.Value);
    }

    [Fact]
    public void Create_ReducesAutomatically_XSquaredBecomesConstant()
    {
        // x^2 mod (x^2 - 2) = 2
        var elem = Elt(X * X, K2);
        Assert.Equal(C(2), elem.Value);
    }

    [Fact]
    public void Create_ReducesAutomatically_XCubedBecomes2X()
    {
        // x^3 mod (x^2 - 2) = 2x
        var elem = Elt(X * X * X, K2);
        Assert.Equal(C(2) * X, elem.Value);
    }

    [Fact]
    public void Create_DegreeZeroUnchanged()
    {
        var elem = Elt(C(5), K2);
        Assert.Equal(C(5), elem.Value);
    }

    [Fact]
    public void AdditiveIdentity_ValueIsZeroPolynomial()
    {
        Assert.True(NumberFieldElement.AdditiveIdentity.Value.IsZero);
    }

    [Fact]
    public void MultiplicativeIdentity_ValueIsOne()
    {
        Assert.Equal(One, NumberFieldElement.MultiplicativeIdentity.Value);
    }

    // =========================================================================
    // NumberFieldElement — arithmetic in Q(√2)
    // =========================================================================

    [Fact]
    public void Addition_AlphaPlusAlpha_Is2Alpha()
    {
        var alpha = NumberFieldElement.Generator(K2);
        var result = alpha + alpha;
        Assert.Equal(C(2) * X, result.Value);
    }

    [Fact]
    public void Addition_AlphaPlusOne_IsXPlusOne()
    {
        var alpha = NumberFieldElement.Generator(K2);
        var result = alpha + Elt(One, K2);
        Assert.Equal(X + One, result.Value);
    }

    [Fact]
    public void Addition_With_AdditiveIdentity_IsNoOp()
    {
        var a = Elt(X + C(3), K2);
        Assert.Equal(a.Value, (a + NumberFieldElement.AdditiveIdentity).Value);
    }

    [Fact]
    public void Subtraction_XPlus1_Minus_XMinus1_Is2()
    {
        var a = Elt(X + One, K2);
        var b = Elt(X - One, K2);
        Assert.Equal(C(2), (a - b).Value);
    }

    [Fact]
    public void Multiplication_Alpha_Times_Alpha_Is2()
    {
        // α² = 2 in Q(√2)
        var alpha = NumberFieldElement.Generator(K2);
        var result = alpha * alpha;
        Assert.Equal(C(2), result.Value);
    }

    [Fact]
    public void Multiplication_XPlus1_Times_XMinus1_Is1()
    {
        // (√2+1)(√2-1) = 2-1 = 1
        var a = Elt(X + One, K2);
        var b = Elt(X - One, K2);
        Assert.Equal(One, (a * b).Value);
    }

    [Fact]
    public void Multiplication_XPlus1_Squared_Is3Plus2X()
    {
        // (√2+1)² = 2+2√2+1 = 3+2√2
        var a = Elt(X + One, K2);
        Assert.Equal(C(3) + C(2) * X, (a * a).Value);
    }

    [Fact]
    public void Multiplication_With_MultiplicativeIdentity_IsNoOp()
    {
        var a = Elt(X + C(3), K2);
        Assert.Equal(a.Value, (a * NumberFieldElement.MultiplicativeIdentity).Value);
    }

    [Fact]
    public void Negation_XPlus1_IsMinusXMinus1()
    {
        var a = Elt(X + One, K2);
        Assert.Equal(-X - One, (-a).Value);
    }

    [Fact]
    public void Multiplication_Associative()
    {
        var a = NumberFieldElement.Generator(K2);
        var b = Elt(X + One, K2);
        var c = Elt(X - One, K2);
        Assert.Equal(((a * b) * c).Value, (a * (b * c)).Value);
    }

    [Fact]
    public void Multiplication_Distributive()
    {
        var a = NumberFieldElement.Generator(K2);
        var b = Elt(One, K2);
        var c = Elt(X - One, K2);
        Assert.Equal(((a + b) * c).Value, (a * c + b * c).Value);
    }

    // =========================================================================
    // NumberFieldElement — inversion
    // =========================================================================

    [Fact]
    public void Invert_Alpha_IsAlphaOverTwo()
    {
        // α⁻¹ = α/2 since α·(α/2) = α²/2 = 1
        var alpha = NumberFieldElement.Generator(K2);
        var inv = NumberFieldElement.Invert(alpha);
        // inv.Value = (1/2)x
        var half = Rational.Create((Integer)1, (Integer)2);
        Assert.Equal(Polynomial<Rational>.C(half) * X, inv.Value);
    }

    [Fact]
    public void Invert_Alpha_RoundTrip_IsOne()
    {
        var alpha = NumberFieldElement.Generator(K2);
        var result = alpha * NumberFieldElement.Invert(alpha);
        Assert.Equal(One, result.Value);
    }

    [Fact]
    public void Invert_XPlus1_IsXMinus1()
    {
        // (√2+1)(√2-1) = 1, so (√2+1)⁻¹ = √2-1
        var a = Elt(X + One, K2);
        var inv = NumberFieldElement.Invert(a);
        Assert.Equal(X - One, inv.Value);
    }

    [Fact]
    public void Invert_XPlus1_RoundTrip_IsOne()
    {
        var a = Elt(X + One, K2);
        Assert.Equal(One, (a * NumberFieldElement.Invert(a)).Value);
    }

    [Fact]
    public void Invert_Constant2_IsOneHalf()
    {
        var two = Elt(C(2), K2);
        var inv = NumberFieldElement.Invert(two);
        var half = Rational.Create((Integer)1, (Integer)2);
        Assert.Equal(Polynomial<Rational>.C(half), inv.Value);
    }

    [Fact]
    public void Invert_SeveralElements_RoundTrip()
    {
        var elements = new[]
        {
            Elt(X + One, K2),
            Elt(C(2) * X + One, K2),
            Elt(C(3) * X - C(5), K2),
            Elt(X, K2),
        };
        foreach (var a in elements)
        {
            var product = a * NumberFieldElement.Invert(a);
            Assert.Equal(One, product.Value);
        }
    }

    [Fact]
    public void Invert_Zero_ReturnsAdditiveIdentity()
    {
        // Convention: Invert(0) = 0 (total function, matching IField contract).
        var inv = NumberFieldElement.Invert(NumberFieldElement.AdditiveIdentity);
        Assert.True(inv.Value.IsZero);
    }

    [Fact]
    public void Division_ByItself_IsOne()
    {
        var a = Elt(X + One, K2);
        Assert.Equal(One, (a / a).Value);
    }

    // =========================================================================
    // NumberFieldElement — Q(i) = Q[x]/(x²+1)
    // =========================================================================

    [Fact]
    public void Qi_ISquared_IsMinusOne()
    {
        var i = NumberFieldElement.Generator(Ki);
        Assert.Equal(-One, (i * i).Value);
    }

    [Fact]
    public void Qi_OnePlusI_Times_OneMinusI_IsTwo()
    {
        var iPlusOne  = Elt(X + One, Ki);
        var iMinusOne = Elt(-X + One, Ki);
        Assert.Equal(C(2), (iPlusOne * iMinusOne).Value);
    }

    [Fact]
    public void Qi_Inverse_Of_I_IsMinusI()
    {
        // i⁻¹ = -i since i·(-i) = 1
        var i = NumberFieldElement.Generator(Ki);
        var inv = NumberFieldElement.Invert(i);
        Assert.Equal(-X, inv.Value);
        Assert.Equal(One, (i * inv).Value);
    }

    [Fact]
    public void Qi_Inverse_Of_1PlusI_Is_1MinusI_Over2()
    {
        // (1+i)⁻¹ = (1-i)/2
        var a = Elt(X + One, Ki);
        var inv = NumberFieldElement.Invert(a);
        var half = Rational.Create((Integer)1, (Integer)2);
        var expected = Polynomial<Rational>.C(half) * (-X + One);
        Assert.Equal(expected, inv.Value);
        Assert.Equal(One, (a * inv).Value);
    }

    // =========================================================================
    // NumberFieldElement — equality
    // =========================================================================

    [Fact]
    public void Equality_SameFieldAndValue_IsEqual()
    {
        Assert.Equal(Elt(X + One, K2), Elt(X + One, K2));
    }

    [Fact]
    public void Equality_DifferentValue_IsNotEqual()
    {
        Assert.NotEqual(Elt(X + One, K2), Elt(X + C(2), K2));
    }

    [Fact]
    public void Equality_SameValueDifferentField_IsNotEqual()
    {
        // x in Q(√2) represents √2; x in Q(i) represents i. Different elements.
        Assert.NotEqual(NumberFieldElement.Generator(K2), NumberFieldElement.Generator(Ki));
    }

    // =========================================================================
    // NumberFieldElement — cross-field mismatch throws
    // =========================================================================

    [Fact]
    public void CrossField_Addition_Throws()
    {
        var a = Elt(X + One, K2);
        var b = Elt(X + One, Ki);
        Assert.Throws<InvalidOperationException>(() => _ = a + b);
    }

    [Fact]
    public void CrossField_Multiplication_Throws()
    {
        var a = Elt(X, K2);
        var b = Elt(X, Ki);
        Assert.Throws<InvalidOperationException>(() => _ = a * b);
    }

    // =========================================================================
    // NumberFieldElement — FromInt
    // =========================================================================

    [Fact]
    public void FromInt_5_HasConstantValue5()
    {
        var elem = NumberFieldElement.FromInt(5);
        Assert.Equal(C(5), elem.Value);
    }

    [Fact]
    public void FromInt_0_IsZeroPolynomial()
    {
        Assert.True(NumberFieldElement.FromInt(0).Value.IsZero);
    }

    // =========================================================================
    // NumberFieldElement — display
    // =========================================================================

    [Fact]
    public void Display_GeneratorWithAlpha_SubstitutesAlpha()
    {
        var K = new NumberField(X * X - C(2), "α");
        var elem = NumberFieldElement.Create(X, K);
        // Polynomial.ToString of x is "x"; after Replace("x","α") → "α"
        Assert.Equal("α", elem.ToString());
    }

    [Fact]
    public void Display_ConstantElement_NoSubstitution()
    {
        var K = new NumberField(X * X - C(2), "α");
        var elem = NumberFieldElement.Create(C(3), K);
        Assert.Equal("3", elem.ToString());
    }

    [Fact]
    public void Display_2Alpha_Substitutes()
    {
        var K = new NumberField(X * X - C(2), "α");
        var elem = NumberFieldElement.Create(C(2) * X, K);
        // "2x" → "2α"
        Assert.Contains("α", elem.ToString());
        Assert.DoesNotContain("x", elem.ToString());
    }
}
