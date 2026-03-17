using Helium.Primitives;

namespace Helium.Primitives.Tests;

public class FromIntTests
{
    // static virtual members must be called through a type parameter.
    private static T FromInt<T>(int n) where T : IRing<T> => T.FromInt(n);

    // --- Integer ---

    [Fact]
    public void Integer_FromInt_Zero() =>
        Assert.Equal(Integer.Zero, FromInt<Integer>(0));

    [Fact]
    public void Integer_FromInt_One() =>
        Assert.Equal(Integer.One, FromInt<Integer>(1));

    [Fact]
    public void Integer_FromInt_Positive() =>
        Assert.Equal((Integer)42, FromInt<Integer>(42));

    [Fact]
    public void Integer_FromInt_Negative() =>
        Assert.Equal((Integer)(-7), FromInt<Integer>(-7));

    [Fact]
    public void Integer_FromInt_Large() =>
        Assert.Equal((Integer)10_000, FromInt<Integer>(10_000));

    // --- Rational ---

    [Fact]
    public void Rational_FromInt_Zero() =>
        Assert.Equal(Rational.Zero, FromInt<Rational>(0));

    [Fact]
    public void Rational_FromInt_Three()
    {
        var r = FromInt<Rational>(3);
        Assert.Equal((Integer)3, r.Numerator);
        Assert.Equal(Integer.One, r.Denominator);
    }

    [Fact]
    public void Rational_FromInt_Negative()
    {
        var r = FromInt<Rational>(-5);
        Assert.Equal((Integer)(-5), r.Numerator);
        Assert.Equal(Integer.One, r.Denominator);
    }

    // --- Complex ---

    [Fact]
    public void Complex_FromInt_Zero() =>
        Assert.Equal(Complex.Zero, FromInt<Complex>(0));

    [Fact]
    public void Complex_FromInt_Seven()
    {
        var c = FromInt<Complex>(7);
        Assert.Equal(7.0, c.Re);
        Assert.Equal(0.0, c.Im);
    }

    // --- Double ---

    [Fact]
    public void Double_FromInt_Zero() =>
        Assert.Equal(Double.Zero, FromInt<Double>(0));

    [Fact]
    public void Double_FromInt_Five() =>
        Assert.Equal(new Double(5.0), FromInt<Double>(5));

    [Fact]
    public void Double_FromInt_Negative() =>
        Assert.Equal(new Double(-3.0), FromInt<Double>(-3));

    // --- Float ---

    [Fact]
    public void Float_FromInt_Two() =>
        Assert.Equal(new Float(2.0f), FromInt<Float>(2));
}
