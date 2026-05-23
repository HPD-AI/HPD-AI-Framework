using Helium.Algebra;
using Helium.Primitives;

namespace Helium.Algebra.Tests;

public class TruncatedPadicTests
{
    [Fact]
    public void Construction_NormalizesModuloPrimePower()
    {
        var value = P5N3(128);

        Assert.Equal((Integer)3, value.Value);
        Assert.Equal((Integer)125, TruncatedPadic<Prime5, Precision3>.Modulus);
        Assert.Equal(3, TruncatedPadic<Prime5, Precision3>.Precision);
    }

    [Fact]
    public void AdditionAndMultiplication_AreModuloPrecision()
    {
        Assert.Equal(P5N3(3), P5N3(120) + P5N3(8));
        Assert.Equal(P5N3(24), P5N3(12) * P5N3(2));
        Assert.Equal(P5N3(124), -P5N3(1));
    }

    [Fact]
    public void RingLaws_HoldForFiniteSamples()
    {
        var a = P5N3(97);
        var b = P5N3(51);
        var c = P5N3(38);

        Assert.Equal((a + b) + c, a + (b + c));
        Assert.Equal(a + b, b + a);
        Assert.Equal((a * b) * c, a * (b * c));
        Assert.Equal(a * b, b * a);
        Assert.Equal(a * (b + c), (a * b) + (a * c));
        Assert.Equal(TruncatedPadic<Prime5, Precision3>.Zero, a + (-a));
    }

    [Fact]
    public void Valuation_AndUnitDetection()
    {
        Assert.Equal(0, P5N3(7).Valuation());
        Assert.True(P5N3(7).IsUnit);

        Assert.Equal(2, P5N3(25).Valuation());
        Assert.False(P5N3(25).IsUnit);

        Assert.Equal(3, P5N3(0).Valuation());
    }

    [Fact]
    public void TryInvert_SucceedsForUnits()
    {
        Assert.True(TruncatedPadic<Prime5, Precision3>.TryInvert(P5N3(2), out var inverse));

        Assert.Equal(TruncatedPadic<Prime5, Precision3>.One, P5N3(2) * inverse);
    }

    [Fact]
    public void TryInvert_FailsForNonUnits()
    {
        Assert.False(TruncatedPadic<Prime5, Precision3>.TryInvert(P5N3(5), out var inverse));
        Assert.Equal(TruncatedPadic<Prime5, Precision3>.Zero, inverse);
    }

    [Fact]
    public void EqualityAndHash_UseNormalizedResidue()
    {
        Assert.Equal(P5N3(3), P5N3(128));
        Assert.Equal(P5N3(3).GetHashCode(), P5N3(128).GetHashCode());
    }

    [Fact]
    public void InvalidPrecisionWitness_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => TruncatedPadic<Prime5, ZeroPrecision>.Modulus);
    }

    private static TruncatedPadic<Prime5, Precision3> P5N3(int value) => new((Integer)value);

    private readonly struct Prime5 : IPrimeModulus
    {
        public static Integer Value => 5;
    }

    private readonly struct Precision3 : IStaticPrecision
    {
        public static int Value => 3;
    }

    private readonly struct ZeroPrecision : IStaticPrecision
    {
        public static int Value => 0;
    }
}
