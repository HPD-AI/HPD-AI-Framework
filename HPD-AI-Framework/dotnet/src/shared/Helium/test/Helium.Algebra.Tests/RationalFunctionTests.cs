using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algebra.Tests;

public class RationalFunctionTests
{
    // Helper: create polynomials over Rational for convenience.
    private static Polynomial<Rational> P(string s) => Polynomial<Rational>.Parse(s);

    // --- Construction ---

    [Fact]
    public void Zero_HasZeroNumerator()
    {
        var z = RationalFunction<Rational>.Zero;
        Assert.True(z.IsZero);
        Assert.True(z.Numerator.IsZero);
        Assert.Equal(Polynomial<Rational>.One, z.Denominator);
    }

    [Fact]
    public void One_HasOneOverOne()
    {
        var one = RationalFunction<Rational>.One;
        Assert.False(one.IsZero);
        Assert.Equal(Polynomial<Rational>.One, one.Numerator);
        Assert.Equal(Polynomial<Rational>.One, one.Denominator);
    }

    [Fact]
    public void FromPolynomial_DenominatorIsOne()
    {
        var p = P("x^2 + 1");
        var rf = RationalFunction<Rational>.FromPolynomial(p);
        Assert.Equal(p, rf.Numerator);
        Assert.Equal(Polynomial<Rational>.One, rf.Denominator);
    }

    [Fact]
    public void Create_StoresNumeratorAndDenominator()
    {
        var num = P("x + 1");
        var den = P("x - 1");
        var rf = RationalFunction<Rational>.Create(num, den);
        Assert.Equal(num, rf.Numerator);
        Assert.Equal(den, rf.Denominator);
    }

    // --- Arithmetic ---

    [Fact]
    public void Addition()
    {
        // 1/x + 1/x = 2/x^2 ... wait, no:
        // (1)/(x) + (1)/(x) via cross multiply = (1*x + 1*x)/(x*x) = 2x/x^2
        // which equals 2/x when reduced.
        // Without normalization, we get 2x / x^2.
        var one = RationalFunction<Rational>.FromPolynomial(Polynomial<Rational>.One);
        var x = RationalFunction<Rational>.Create(Polynomial<Rational>.One, P("x"));
        var sum = x + x;

        // 1/x + 1/x = 2x / x^2 (un-normalized)
        Assert.Equal(P("2x"), sum.Numerator);
        Assert.Equal(P("x^2"), sum.Denominator);
    }

    [Fact]
    public void Addition_DifferentDenominators()
    {
        // 1/(x+1) + 1/(x-1) = ((x-1) + (x+1)) / ((x+1)(x-1)) = 2x / (x^2 - 1)
        var a = RationalFunction<Rational>.Create(Polynomial<Rational>.One, P("x + 1"));
        var b = RationalFunction<Rational>.Create(Polynomial<Rational>.One, P("x - 1"));
        var sum = a + b;

        Assert.Equal(P("2x"), sum.Numerator);
        Assert.Equal(P("x^2 - 1"), sum.Denominator);
    }

    [Fact]
    public void Subtraction()
    {
        // x/1 - 1/1 = (x - 1)/1
        var a = RationalFunction<Rational>.FromPolynomial(P("x"));
        var b = RationalFunction<Rational>.FromPolynomial(Polynomial<Rational>.One);
        var diff = a - b;

        Assert.Equal(P("x - 1"), diff.Numerator);
        Assert.Equal(Polynomial<Rational>.One, diff.Denominator);
    }

    [Fact]
    public void Multiplication()
    {
        // (x+1)/1 * 1/(x-1) = (x+1)/(x-1)
        var a = RationalFunction<Rational>.FromPolynomial(P("x + 1"));
        var b = RationalFunction<Rational>.Create(Polynomial<Rational>.One, P("x - 1"));
        var product = a * b;

        Assert.Equal(P("x + 1"), product.Numerator);
        Assert.Equal(P("x - 1"), product.Denominator);
    }

    [Fact]
    public void Negation()
    {
        var rf = RationalFunction<Rational>.Create(P("x + 1"), P("x - 1"));
        var neg = -rf;

        Assert.Equal(P("-x - 1"), neg.Numerator);
        Assert.Equal(P("x - 1"), neg.Denominator);
    }

    [Fact]
    public void AdditiveIdentity()
    {
        var rf = RationalFunction<Rational>.Create(P("x + 1"), P("x - 1"));
        var sum = rf + RationalFunction<Rational>.Zero;
        Assert.Equal(rf, sum);
    }

    [Fact]
    public void MultiplicativeIdentity()
    {
        var rf = RationalFunction<Rational>.Create(P("x + 1"), P("x - 1"));
        var product = rf * RationalFunction<Rational>.One;
        Assert.Equal(rf, product);
    }

    // --- Equality (cross-multiply) ---

    [Fact]
    public void Equality_SameRepresentation()
    {
        var a = RationalFunction<Rational>.Create(P("x + 1"), P("x - 1"));
        var b = RationalFunction<Rational>.Create(P("x + 1"), P("x - 1"));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_CrossMultiply()
    {
        // (2x + 2)/(2x - 2) == (x + 1)/(x - 1) because (2x+2)(x-1) == (x+1)(2x-2)
        var a = RationalFunction<Rational>.Create(P("2x + 2"), P("2x - 2"));
        var b = RationalFunction<Rational>.Create(P("x + 1"), P("x - 1"));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_ZeroNumerator()
    {
        var a = RationalFunction<Rational>.Create(Polynomial<Rational>.Zero, P("x + 1"));
        var b = RationalFunction<Rational>.Create(Polynomial<Rational>.Zero, P("x^2 + 5"));
        Assert.Equal(a, b); // Both are 0.
    }

    [Fact]
    public void Inequality()
    {
        var a = RationalFunction<Rational>.Create(P("x"), P("x + 1"));
        var b = RationalFunction<Rational>.Create(P("x"), P("x - 1"));
        Assert.NotEqual(a, b);
    }

    // --- Field operations (Reduce, Divide) ---

    [Fact]
    public void Reduce_RemovesCommonFactor()
    {
        // (x^2 - 1) / (x + 1) reduces to (x - 1) / 1
        var rf = RationalFunction<Rational>.Create(P("x^2 - 1"), P("x + 1"));
        var reduced = rf.Reduce();

        Assert.Equal(Polynomial<Rational>.One, reduced.Denominator);
        Assert.Equal(P("x - 1"), reduced.Numerator);
    }

    [Fact]
    public void Reduce_AlreadyReduced()
    {
        var rf = RationalFunction<Rational>.Create(P("x + 1"), P("x - 1"));
        var reduced = rf.Reduce();

        Assert.Equal(P("x + 1"), reduced.Numerator);
        Assert.Equal(P("x - 1"), reduced.Denominator);
    }

    [Fact]
    public void Reduce_ZeroNumerator()
    {
        var rf = RationalFunction<Rational>.Create(Polynomial<Rational>.Zero, P("x + 1"));
        var reduced = rf.Reduce();
        Assert.True(reduced.IsZero);
    }

    [Fact]
    public void Divide()
    {
        // (x+1)/(x-1) divided by (x+1)/1 = (x+1)*1 / ((x-1)*(x+1)) = 1/(x-1) when reduced
        var a = RationalFunction<Rational>.Create(P("x + 1"), P("x - 1"));
        var b = RationalFunction<Rational>.FromPolynomial(P("x + 1"));
        var quotient = a.Divide(b);

        // Without reduction: (x+1) / ((x-1)(x+1)) = (x+1)/(x^2-1)
        // Cross-multiply equality: (x+1)*1 == 1*(x^2-1)? No. But:
        // a/b = (x+1)/(x-1), c/d = (x+1)/1
        // result = (a*d)/(b*c) = ((x+1)*1)/((x-1)*(x+1)) = (x+1)/(x^2-1)
        Assert.Equal(P("x + 1"), quotient.Numerator);
        Assert.Equal(P("x^2 - 1"), quotient.Denominator);

        // After reduction, should be 1/(x-1)
        var reduced = quotient.Reduce();
        Assert.Equal(Polynomial<Rational>.One, reduced.Numerator);
        Assert.Equal(P("x - 1"), reduced.Denominator);
    }

    [Fact]
    public void Divide_ByZero_ReturnsZero()
    {
        var a = RationalFunction<Rational>.Create(P("x + 1"), P("x - 1"));
        var b = RationalFunction<Rational>.Zero;
        var result = a.Divide(b);
        Assert.True(result.IsZero);
    }

    // --- Normalized factory ---

    [Fact]
    public void NormalizedFactory_AutoReduces()
    {
        // (x^2 - 1) / (x + 1) should auto-reduce to (x - 1) / 1
        var rf = RationalFunctionField.Of(P("x^2 - 1"), P("x + 1"));
        Assert.Equal(P("x - 1"), rf.Numerator);
        Assert.Equal(Polynomial<Rational>.One, rf.Denominator);
    }

    [Fact]
    public void NormalizedFactory_ZeroDenominator()
    {
        var rf = RationalFunctionField.Of(P("x + 1"), Polynomial<Rational>.Zero);
        Assert.True(rf.IsZero);
        Assert.Equal(Polynomial<Rational>.One, rf.Denominator);
    }

    [Fact]
    public void NormalizedFactory_ZeroNumerator()
    {
        var rf = RationalFunctionField.Of(Polynomial<Rational>.Zero, P("x + 1"));
        Assert.True(rf.IsZero);
    }

    [Fact]
    public void NormalizedFactory_ArithmeticPreservesNormalization()
    {
        var a = RationalFunctionField.Of(P("x^2 - 1"), P("x + 1")); // reduces to (x-1)/1
        var b = RationalFunctionField.Of(P("x"), P("x"));             // reduces to 1/1

        var sum = a + b; // (x-1)/1 + 1/1 = x/1, auto-reduced
        Assert.Equal(P("x"), sum.Numerator);
        Assert.Equal(Polynomial<Rational>.One, sum.Denominator);
    }

    // --- Formatting ---

    [Fact]
    public void Format_Default_PolynomialDenominator()
    {
        var rf = RationalFunction<Rational>.Create(P("x + 1"), P("x - 1"));
        var s = rf.ToString();
        Assert.Equal("(x + 1)/(x - 1)", s);
    }

    [Fact]
    public void Format_Default_DenominatorIsOne()
    {
        var rf = RationalFunction<Rational>.FromPolynomial(P("x^2 + 1"));
        Assert.Equal("x^2 + 1", rf.ToString());
    }

    [Fact]
    public void Format_Default_Zero()
    {
        Assert.Equal("0", RationalFunction<Rational>.Zero.ToString());
    }

    [Fact]
    public void Format_Latex()
    {
        var rf = RationalFunction<Rational>.Create(P("x + 1"), P("x - 1"));
        var s = rf.ToString("L", null);
        Assert.Equal("\\frac{x + 1}{x - 1}", s);
    }

    [Fact]
    public void Format_MathML()
    {
        var rf = RationalFunction<Rational>.Create(P("x + 1"), P("x - 1"));
        var s = rf.ToString("M", null);
        Assert.Contains("<mfrac>", s);
    }

    // --- Parsing ---

    [Fact]
    public void Parse_SimpleRationalFunction()
    {
        var rf = RationalFunction<Rational>.Parse("(x + 1)/(x - 1)");
        Assert.Equal(P("x + 1"), rf.Numerator);
        Assert.Equal(P("x - 1"), rf.Denominator);
    }

    [Fact]
    public void Parse_PolynomialOnly()
    {
        var rf = RationalFunction<Rational>.Parse("x^2 + 1");
        Assert.Equal(P("x^2 + 1"), rf.Numerator);
        Assert.Equal(Polynomial<Rational>.One, rf.Denominator);
    }

    [Fact]
    public void TryParse_Valid()
    {
        Assert.True(RationalFunction<Rational>.TryParse("(x + 1)/(x - 1)", null, out var rf));
        Assert.Equal(P("x + 1"), rf.Numerator);
        Assert.Equal(P("x - 1"), rf.Denominator);
    }

    [Fact]
    public void TryParse_Null_ReturnsFalse()
    {
        Assert.False(RationalFunction<Rational>.TryParse(null, null, out var rf));
        Assert.True(rf.IsZero);
    }
}
