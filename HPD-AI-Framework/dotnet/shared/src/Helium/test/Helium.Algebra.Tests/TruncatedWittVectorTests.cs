using Helium.Algebra;
using Helium.Primitives;

namespace Helium.Algebra.Tests;

public class TruncatedWittVectorTests
{
    [Fact]
    public void ZeroAndOne()
    {
        Assert.Equal(W(0, 0), TruncatedWittVector<Integer, Prime2, Length2>.Zero);
        Assert.Equal(W(1, 0), TruncatedWittVector<Integer, Prime2, Length2>.One);
    }

    [Fact]
    public void ComponentAccess()
    {
        var value = W(3, 4);

        Assert.Equal((Integer)3, value[0]);
        Assert.Equal((Integer)4, value[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => value[2]);
    }

    [Fact]
    public void Addition_IsNotNaiveComponentwise()
    {
        var sum = W(1, 0) + W(1, 0);

        Assert.Equal(W(2, -1), sum);
        Assert.NotEqual(W(2, 0), sum);
    }

    [Fact]
    public void Multiplication_IsNotNaiveComponentwise()
    {
        var product = W(2, 1) * W(3, 1);

        Assert.Equal(W(6, 15), product);
        Assert.NotEqual(W(6, 1), product);
    }

    [Fact]
    public void GhostComponents_PreserveAdditionAndMultiplication()
    {
        var left = W(2, 1);
        var right = W(3, 4);
        var sum = left + right;
        var product = left * right;

        Assert.Equal(left.GhostComponent(0) + right.GhostComponent(0), sum.GhostComponent(0));
        Assert.Equal(left.GhostComponent(1) + right.GhostComponent(1), sum.GhostComponent(1));
        Assert.Equal(left.GhostComponent(0) * right.GhostComponent(0), product.GhostComponent(0));
        Assert.Equal(left.GhostComponent(1) * right.GhostComponent(1), product.GhostComponent(1));
    }

    [Fact]
    public void RingLaws_HoldForLengthTwoIntegerSamples()
    {
        var a = W(2, 1);
        var b = W(3, -2);
        var c = W(-1, 4);

        Assert.Equal((a + b) + c, a + (b + c));
        Assert.Equal(a + b, b + a);
        Assert.Equal((a * b) * c, a * (b * c));
        Assert.Equal(a * b, b * a);
        Assert.Equal(a * (b + c), (a * b) + (a * c));
        Assert.Equal(TruncatedWittVector<Integer, Prime2, Length2>.Zero, a + (-a));
        Assert.Equal(a, a * TruncatedWittVector<Integer, Prime2, Length2>.One);
    }

    [Fact]
    public void Truncate_PreservesPrefix()
    {
        var value = W(7, 9);
        var truncated = value.Truncate<Length1>();

        Assert.Equal((Integer)7, truncated[0]);
        Assert.Equal(1, TruncatedWittVector<Integer, Prime2, Length1>.Length);
    }

    [Fact]
    public void EqualityAndHash_AreStructural()
    {
        Assert.Equal(W(1, 2), W(1, 2));
        Assert.NotEqual(W(1, 2), W(2, 1));
        Assert.Equal(W(1, 2).GetHashCode(), W(1, 2).GetHashCode());
    }

    [Fact]
    public void Constructor_RejectsWrongComponentCount()
    {
        Assert.Throws<ArgumentException>(() => new TruncatedWittVector<Integer, Prime2, Length2>([(Integer)1]));
    }

    [Fact]
    public void InvalidLengthWitness_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => TruncatedWittVector<Integer, Prime2, ZeroLength>.Length);
        Assert.Throws<NotSupportedException>(() => TruncatedWittVector<Integer, Prime2, Length3>.Length);
    }

    private static TruncatedWittVector<Integer, Prime2, Length2> W(int a, int b) =>
        new([(Integer)a, (Integer)b]);

    private readonly struct Prime2 : IPrimeModulus
    {
        public static Integer Value => 2;
    }

    private readonly struct Length1 : IStaticPrecision
    {
        public static int Value => 1;
    }

    private readonly struct Length2 : IStaticPrecision
    {
        public static int Value => 2;
    }

    private readonly struct Length3 : IStaticPrecision
    {
        public static int Value => 3;
    }

    private readonly struct ZeroLength : IStaticPrecision
    {
        public static int Value => 0;
    }
}
