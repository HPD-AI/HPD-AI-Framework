using Helium.Primitives;

namespace Helium.Primitives.Tests;

public class FormalPowerSeriesTests
{
    // --- Construction ---

    [Fact]
    public void ZeroSeriesAllCoefficientsZero()
    {
        var zero = FormalPowerSeries<Rational>.Zero;
        for (int i = 0; i < 10; i++)
            Assert.Equal(Rational.Zero, zero.Coefficient(i));
    }

    [Fact]
    public void OneSeriesConstantOne()
    {
        var one = FormalPowerSeries<Rational>.One;
        Assert.Equal(Rational.One, one.Coefficient(0));
        for (int i = 1; i < 10; i++)
            Assert.Equal(Rational.Zero, one.Coefficient(i));
    }

    [Fact]
    public void XSeries()
    {
        var x = FormalPowerSeries<Rational>.X;
        Assert.Equal(Rational.Zero, x.Coefficient(0));
        Assert.Equal(Rational.One, x.Coefficient(1));
        Assert.Equal(Rational.Zero, x.Coefficient(2));
    }

    [Fact]
    public void FromGenerator()
    {
        // 1 + x + x^2 + x^3 + ... (all coefficients 1)
        var f = FormalPowerSeries<Rational>.FromGenerator(_ => Rational.One);
        for (int i = 0; i < 10; i++)
            Assert.Equal(Rational.One, f.Coefficient(i));
    }

    [Fact]
    public void NegativeIndexReturnsZero()
    {
        var f = FormalPowerSeries<Rational>.FromGenerator(n => (Rational)n);
        Assert.Equal(Rational.Zero, f.Coefficient(-1));
        Assert.Equal(Rational.Zero, f.Coefficient(-100));
    }

    // --- Arithmetic ---

    [Fact]
    public void Addition()
    {
        var f = FormalPowerSeries<Rational>.FromGenerator(n => (Rational)n);      // 0 + x + 2x^2 + ...
        var g = FormalPowerSeries<Rational>.FromGenerator(n => (Rational)(n * n)); // 0 + x + 4x^2 + 9x^3 + ...
        var sum = f + g;
        Assert.Equal(Rational.Zero, sum.Coefficient(0));           // 0+0
        Assert.Equal(Rational.Create((Integer)2, Integer.One), sum.Coefficient(1)); // 1+1
        Assert.Equal(Rational.Create((Integer)6, Integer.One), sum.Coefficient(2)); // 2+4
    }

    [Fact]
    public void Subtraction()
    {
        var f = FormalPowerSeries<Rational>.FromGenerator(_ => Rational.One);
        var g = FormalPowerSeries<Rational>.FromGenerator(_ => Rational.One);
        var diff = f - g;
        for (int i = 0; i < 10; i++)
            Assert.Equal(Rational.Zero, diff.Coefficient(i));
    }

    [Fact]
    public void Negation()
    {
        var f = FormalPowerSeries<Rational>.FromGenerator(n => (Rational)n);
        var neg = -f;
        Assert.Equal(Rational.Zero, neg.Coefficient(0));
        Assert.Equal((Rational)(-1), neg.Coefficient(1));
        Assert.Equal((Rational)(-2), neg.Coefficient(2));
    }

    // --- Cauchy product ---

    [Fact]
    public void CauchyProductOnePlusXSquared()
    {
        // (1 + x) * (1 + x) = 1 + 2x + x^2
        var onePlusX = FormalPowerSeries<Rational>.FromGenerator(n => n <= 1 ? Rational.One : Rational.Zero);
        var product = onePlusX * onePlusX;
        Assert.Equal(Rational.One, product.Coefficient(0));
        Assert.Equal((Rational)2, product.Coefficient(1));
        Assert.Equal(Rational.One, product.Coefficient(2));
        Assert.Equal(Rational.Zero, product.Coefficient(3));
    }

    [Fact]
    public void CauchyProductIdentity()
    {
        var f = FormalPowerSeries<Rational>.FromGenerator(n => (Rational)(n + 1));
        var product = f * FormalPowerSeries<Rational>.One;
        for (int i = 0; i < 10; i++)
            Assert.Equal(f.Coefficient(i), product.Coefficient(i));
    }

    [Fact]
    public void CauchyProductZero()
    {
        var f = FormalPowerSeries<Rational>.FromGenerator(n => (Rational)(n + 1));
        var product = f * FormalPowerSeries<Rational>.Zero;
        for (int i = 0; i < 10; i++)
            Assert.Equal(Rational.Zero, product.Coefficient(i));
    }

    // --- Inverse ---

    [Fact]
    public void GeometricSeriesInverse()
    {
        // 1/(1-x) should have all coefficients 1.
        // 1-x as power series:
        var oneMinusX = FormalPowerSeries<Rational>.FromGenerator(n => n switch
        {
            0 => Rational.One,
            1 => (Rational)(-1),
            _ => Rational.Zero
        });
        var inv = oneMinusX.Inverse();
        for (int i = 0; i < 20; i++)
            Assert.Equal(Rational.One, inv.Coefficient(i));
    }

    [Fact]
    public void InverseTimesOriginalIsOne()
    {
        // f = 1 + x + x^2 (truncated at degree 2 for simplicity in generator)
        var f = FormalPowerSeries<Rational>.FromGenerator(n => n <= 2 ? Rational.One : Rational.Zero);
        var inv = f.Inverse();
        var product = f * inv;
        // First N terms should be 1, 0, 0, 0, ...
        Assert.Equal(Rational.One, product.Coefficient(0));
        for (int i = 1; i < 15; i++)
            Assert.Equal(Rational.Zero, product.Coefficient(i));
    }

    // --- Composition ---

    [Fact]
    public void ComposeWithZeroGivesConstantTerm()
    {
        var f = FormalPowerSeries<Rational>.FromGenerator(n => (Rational)(n + 1)); // 1 + 2x + 3x^2 + ...
        var result = FormalPowerSeries<Rational>.Compose(f, FormalPowerSeries<Rational>.Zero);
        Assert.Equal(Rational.One, result.Coefficient(0));
        for (int i = 1; i < 10; i++)
            Assert.Equal(Rational.Zero, result.Coefficient(i));
    }

    [Fact]
    public void ComposeWithIdentity()
    {
        var f = FormalPowerSeries<Rational>.FromGenerator(n => n <= 3 ? (Rational)(n + 1) : Rational.Zero);
        var result = FormalPowerSeries<Rational>.Compose(f, FormalPowerSeries<Rational>.X);
        for (int i = 0; i <= 5; i++)
            Assert.Equal(f.Coefficient(i), result.Coefficient(i));
    }

    // --- Memoization ---

    [Fact]
    public void MemoizationReturnsConsistentValues()
    {
        int callCount = 0;
        var f = FormalPowerSeries<Rational>.FromGenerator(n =>
        {
            Interlocked.Increment(ref callCount);
            return (Rational)n;
        });

        var first = f.Coefficient(5);
        var callsAfterFirst = callCount;
        var second = f.Coefficient(5);
        Assert.Equal(first, second);
        Assert.Equal(callsAfterFirst, callCount); // No additional call
    }

    // --- GetCoefficients ---

    [Fact]
    public void GetCoefficientsReturnsCorrectArray()
    {
        var f = FormalPowerSeries<Rational>.FromGenerator(n => (Rational)n);
        var coeffs = f.GetCoefficients(5);
        Assert.Equal(5, coeffs.Length);
        for (int i = 0; i < 5; i++)
            Assert.Equal((Rational)i, coeffs[i]);
    }
}
