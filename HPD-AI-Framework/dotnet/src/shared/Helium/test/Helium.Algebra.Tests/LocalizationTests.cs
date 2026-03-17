using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algebra.Tests;

public class LocalizationTests
{
    // --- Rational as the canonical integer fraction type ---

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
        var r = Rational.Create((Integer)3, (Integer)(-6));
        Assert.Equal((Integer)(-1), r.Numerator);
        Assert.Equal((Integer)2, r.Denominator);
    }

    [Fact]
    public void Addition()
    {
        var a = Rational.Create((Integer)1, (Integer)2);
        var b = Rational.Create((Integer)1, (Integer)3);
        var sum = a + b;
        // 1/2 + 1/3 = 5/6
        Assert.Equal(Rational.Create((Integer)5, (Integer)6), sum);
    }

    [Fact]
    public void Multiplication()
    {
        var a = Rational.Create((Integer)2, (Integer)3);
        var b = Rational.Create((Integer)3, (Integer)4);
        var product = a * b;
        // 2/3 * 3/4 = 6/12 = 1/2
        Assert.Equal(Rational.Create((Integer)1, (Integer)2), product);
    }

    [Fact]
    public void ZeroDenominator()
    {
        // Rational uses total-function convention: 5/0 = 0
        var r = Rational.Create((Integer)5, (Integer)0);
        Assert.Equal(Rational.Zero, r);
    }

    [Fact]
    public void EqualityCrossMultiply()
    {
        var a = Rational.Create((Integer)1, (Integer)2);
        var b = Rational.Create((Integer)2, (Integer)4);
        Assert.Equal(a, b);
    }

    [Fact]
    public void EmbedInteger()
    {
        var a = Rational.Create((Integer)5, (Integer)1);
        Assert.Equal((Integer)5, a.Numerator);
        Assert.Equal((Integer)1, a.Denominator);
    }

    [Fact]
    public void LocalizationGenericArithmetic()
    {
        // Localization<Integer> with GCD normalization
        var a = Localization<Integer>.Create((Integer)1, (Integer)2, (n, d) =>
        {
            if (d.IsZero) return (Integer.Zero, Integer.One);
            var g = Integer.Gcd(n.Abs(), d.Abs());
            return g.IsZero ? (n, d) : (n / g, d / g);
        });
        var b = Localization<Integer>.Create((Integer)1, (Integer)3, (n, d) =>
        {
            if (d.IsZero) return (Integer.Zero, Integer.One);
            var g = Integer.Gcd(n.Abs(), d.Abs());
            return g.IsZero ? (n, d) : (n / g, d / g);
        });
        var sum = a + b;
        // 1/2 + 1/3 = 5/6
        Assert.Equal((Integer)5, sum.Numerator);
        Assert.Equal((Integer)6, sum.Denominator);
    }
}
