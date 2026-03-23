using Helium.Primitives;
using Helium.Primitives.Tests.Axioms;

namespace Helium.Primitives.Tests;

public class RationalTests
{
    // --- Field axioms ---

    [Theory]
    [MemberData(nameof(Triples))]
    public void FieldAxioms(Rational a, Rational b, Rational c) =>
        Axioms.FieldAxioms.VerifyAll(a, b, c);

    // --- Canonical form ---

    [Fact]
    public void AutoReduces()
    {
        var r = Rational.Create((Integer)2, (Integer)4);
        Assert.Equal((Integer)1, r.Numerator);
        Assert.Equal((Integer)2, r.Denominator);
    }

    [Fact]
    public void SignNormalization()
    {
        var r = Rational.Create((Integer)6, (Integer)(-9));
        Assert.Equal((Integer)(-2), r.Numerator);
        Assert.Equal((Integer)3, r.Denominator);
    }

    [Fact]
    public void ZeroNumerator()
    {
        var r = Rational.Create((Integer)0, (Integer)5);
        Assert.True(r.IsZero);
        Assert.Equal((Integer)0, r.Numerator);
        Assert.Equal((Integer)1, r.Denominator);
    }

    [Fact]
    public void ZeroDenominator()
    {
        var r = Rational.Create((Integer)5, (Integer)0);
        Assert.True(r.IsZero);
    }

    [Fact]
    public void EqualityViaCanonicalForm()
    {
        var a = Rational.Create((Integer)1, (Integer)2);
        var b = Rational.Create((Integer)2, (Integer)4);
        var c = Rational.Create((Integer)3, (Integer)6);
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void CanonicalFormPreservedAfterArithmetic()
    {
        var a = Rational.Create((Integer)1, (Integer)3);
        var b = Rational.Create((Integer)1, (Integer)6);
        var sum = a + b;
        // 1/3 + 1/6 = 1/2
        Assert.Equal((Integer)1, sum.Numerator);
        Assert.Equal((Integer)2, sum.Denominator);
    }

    // --- Arithmetic ---

    [Fact]
    public void BasicArithmetic()
    {
        var half = Rational.Create((Integer)1, (Integer)2);
        var third = Rational.Create((Integer)1, (Integer)3);

        Assert.Equal(Rational.Create((Integer)5, (Integer)6), half + third);
        Assert.Equal(Rational.Create((Integer)1, (Integer)6), half - third);
        Assert.Equal(Rational.Create((Integer)1, (Integer)6), half * third);
        Assert.Equal(Rational.Create((Integer)3, (Integer)2), half / third);
    }

    [Fact]
    public void InvertZero()
    {
        Assert.Equal(Rational.Zero, Rational.Invert(Rational.Zero));
    }

    [Fact]
    public void InvertInvert()
    {
        var r = Rational.Create((Integer)3, (Integer)7);
        Assert.Equal(r, Rational.Invert(Rational.Invert(r)));
    }

    // --- Ordering ---

    [Fact]
    public void Ordering()
    {
        var third = Rational.Create((Integer)1, (Integer)3);
        var half = Rational.Create((Integer)1, (Integer)2);
        Assert.True(third < half);
        Assert.True(half > third);
        Assert.True(Rational.Zero <= Rational.One);
    }

    // --- ICharP ---

    [Fact]
    public void CharacteristicIsZero() => Assert.Equal(0, Rational.Characteristic);

    // --- Test data ---

    public static TheoryData<Rational, Rational, Rational> Triples() => new()
    {
        { Rational.Zero, Rational.One, Rational.Create((Integer)1, (Integer)2) },
        { Rational.Create((Integer)2, (Integer)3), Rational.Create((Integer)(-5), (Integer)7), Rational.Create((Integer)3, (Integer)11) },
        { Rational.One, Rational.Create((Integer)(-1), (Integer)1), Rational.Create((Integer)1, (Integer)100) },
    };
}
