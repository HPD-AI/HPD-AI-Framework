using Helium.Primitives;

namespace Helium.Primitives.Tests;

public class ComplexTests
{
    private static Rational R(int n) => (Rational)n;
    private static Rational R(int num, int den) => Rational.Create((Integer)num, (Integer)den);
    private static Complex<Rational> C(int re, int im) => new(R(re), R(im));

    [Fact]
    public void Add()
    {
        Assert.Equal(C(4, 6), C(1, 2) + C(3, 4));
    }

    [Fact]
    public void Multiply()
    {
        Assert.Equal(C(-5, 10), C(1, 2) * C(3, 4));
    }

    [Fact]
    public void ImaginaryUnitSquared()
    {
        Assert.Equal(new Complex<Rational>(R(-1), Rational.Zero),
            Complex<Rational>.I * Complex<Rational>.I);
    }

    [Fact]
    public void Invert()
    {
        var z = C(3, 4);
        var inv = Complex<Rational>.Invert(z);
        Assert.Equal(Complex<Rational>.One, z * inv);
        Assert.Equal(new Complex<Rational>(R(3, 25), R(-4, 25)), inv);
    }

    [Fact]
    public void InvertZero_IsZero()
    {
        Assert.Equal(Complex<Rational>.Zero, Complex<Rational>.Invert(Complex<Rational>.Zero));
    }

    [Fact]
    public void Star_Conjugates()
    {
        Assert.Equal(C(3, -4), Complex<Rational>.Star(C(3, 4)));
    }

    [Fact]
    public void Star_Involution()
    {
        var z = C(3, 4);
        Assert.Equal(z, Complex<Rational>.Star(Complex<Rational>.Star(z)));
    }

    [Fact]
    public void Star_DistributesOverAddition()
    {
        var z = C(1, 2);
        var w = C(3, 4);
        Assert.Equal(Complex<Rational>.Star(z) + Complex<Rational>.Star(w), Complex<Rational>.Star(z + w));
    }

    [Fact]
    public void NormProduct_IsReal()
    {
        var z = C(3, 4);
        Assert.Equal(new Complex<Rational>(R(25), Rational.Zero), z * Complex<Rational>.Star(z));
    }

    [Fact]
    public void RingAxioms()
    {
        var a = C(1, 2);
        var b = C(3, -4);
        var c = C(-2, 5);

        Assert.Equal(a, a + Complex<Rational>.Zero);
        Assert.Equal(a, a * Complex<Rational>.One);
        Assert.Equal(Complex<Rational>.Zero, a + (-a));
        Assert.Equal((a + b) + c, a + (b + c));
        Assert.Equal(a + b, b + a);
        Assert.Equal(a * (b + c), a * b + a * c);
    }

    [Fact]
    public void FromInt()
    {
        var c = Complex<Rational>.FromInt(7);
        Assert.Equal(R(7), c.Re);
        Assert.Equal(Rational.Zero, c.Im);
    }

    [Fact]
    public void Formatting()
    {
        Assert.Equal("5", new Complex<Rational>(R(5), Rational.Zero).ToString());
        Assert.Equal("i", Complex<Rational>.I.ToString());
        Assert.Equal("-i", new Complex<Rational>(Rational.Zero, R(-1)).ToString());
        Assert.Equal("3 + 4i", C(3, 4).ToString());
        Assert.Equal("3 - 4i", C(3, -4).ToString());
    }
}
