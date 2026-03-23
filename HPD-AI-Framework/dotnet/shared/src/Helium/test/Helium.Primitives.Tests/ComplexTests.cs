using Helium.Primitives;
using Helium.Primitives.Tests.Axioms;
using Complex = Helium.Primitives.Complex;

namespace Helium.Primitives.Tests;

public class ComplexTests
{
    private static void AssertApprox(Complex expected, Complex actual, double tol = 1e-10)
    {
        Assert.True(Math.Abs(expected.Re - actual.Re) < tol,
            $"Re: expected {expected.Re}, got {actual.Re}");
        Assert.True(Math.Abs(expected.Im - actual.Im) < tol,
            $"Im: expected {expected.Im}, got {actual.Im}");
    }

    // --- Basic arithmetic ---

    [Fact]
    public void Addition()
    {
        var a = new Complex(1.0, 2.0);
        var b = new Complex(3.0, 4.0);
        Assert.Equal(new Complex(4.0, 6.0), a + b);
    }

    [Fact]
    public void Multiplication()
    {
        var a = new Complex(1.0, 2.0);
        var b = new Complex(3.0, 4.0);
        // (1+2i)(3+4i) = 3+4i+6i+8i^2 = (3-8)+(4+6)i = -5+10i
        Assert.Equal(new Complex(-5.0, 10.0), a * b);
    }

    [Fact]
    public void ISquaredIsMinusOne()
    {
        var i = Complex.I;
        Assert.Equal(new Complex(-1.0, 0.0), i * i);
    }

    [Fact]
    public void Inversion()
    {
        var z = new Complex(3.0, 4.0);
        var inv = Complex.Invert(z);
        AssertApprox(Complex.One, z * inv);
    }

    [Fact]
    public void InvertZero()
    {
        Assert.Equal(Complex.Zero, Complex.Invert(Complex.Zero));
    }

    // --- Star (conjugation) ---

    [Fact]
    public void Conjugation()
    {
        var z = new Complex(3.0, 4.0);
        Assert.Equal(new Complex(3.0, -4.0), Complex.Star(z));
    }

    [Fact]
    public void StarIsInvolution()
    {
        var z = new Complex(3.0, 4.0);
        Assert.Equal(z, Complex.Star(Complex.Star(z)));
    }

    [Fact]
    public void StarIsAdditive()
    {
        var z = new Complex(1.0, 2.0);
        var w = new Complex(3.0, 4.0);
        Assert.Equal(Complex.Star(z) + Complex.Star(w), Complex.Star(z + w));
    }

    [Fact]
    public void ZTimesStarZIsRealNonNegative()
    {
        var z = new Complex(3.0, 4.0);
        var product = z * Complex.Star(z);
        Assert.True(Math.Abs(product.Im) < 1e-10);
        Assert.True(product.Re >= 0);
        Assert.True(Math.Abs(product.Re - 25.0) < 1e-10); // 3^2 + 4^2
    }

    // --- Ring axioms (approximate for floating-point) ---

    [Theory]
    [MemberData(nameof(Triples))]
    public void RingAxiomsApprox(Complex a, Complex b, Complex c)
    {
        AssertApprox(a + b, b + a);
        AssertApprox((a + b) + c, a + (b + c));
        AssertApprox((a * b) * c, a * (b * c));
        AssertApprox(a * (b + c), a * b + a * c);
        AssertApprox(a + Complex.Zero, a);
        AssertApprox(a * Complex.One, a);
        AssertApprox(a + (-a), Complex.Zero);
    }

    // --- ICharP ---

    [Fact]
    public void CharacteristicIsZero() => Assert.Equal(0, Complex.Characteristic);

    // --- Test data ---

    public static TheoryData<Complex, Complex, Complex> Triples() => new()
    {
        { new Complex(1, 0), new Complex(0, 1), new Complex(1, 1) },
        { new Complex(2, -3), new Complex(-1, 4), new Complex(0.5, 0.5) },
        { Complex.Zero, Complex.One, Complex.I },
    };
}
