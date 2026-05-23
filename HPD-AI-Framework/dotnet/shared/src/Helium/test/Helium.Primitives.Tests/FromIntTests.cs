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

    // --- Complex<Rational> ---

    [Fact]
    public void Complex_FromInt_Zero() =>
        Assert.Equal(Complex<Rational>.Zero, FromInt<Complex<Rational>>(0));

    [Fact]
    public void Complex_FromInt_Seven()
    {
        var c = FromInt<Complex<Rational>>(7);
        Assert.Equal((Rational)7, c.Re);
        Assert.Equal(Rational.Zero, c.Im);
    }
}
