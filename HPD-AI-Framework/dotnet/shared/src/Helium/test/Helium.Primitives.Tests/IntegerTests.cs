using System.Numerics;
using Helium.Primitives;
using Helium.Primitives.Tests.Axioms;

namespace Helium.Primitives.Tests;

public class IntegerTests
{
    // --- Ring axioms ---

    [Theory]
    [MemberData(nameof(Triples))]
    public void RingAxioms(Integer a, Integer b, Integer c) =>
        Axioms.RingAxioms.VerifyAll(a, b, c);

    [Theory]
    [MemberData(nameof(Triples))]
    public void CommRingAxioms(Integer a, Integer b, Integer c) =>
        Axioms.CommRingAxioms.VerifyAll(a, b, c);

    // --- Construction ---

    [Fact]
    public void FromInt() => Assert.Equal(Integer.Zero + (Integer)42, (Integer)42);

    [Fact]
    public void FromString() => Assert.Equal(Integer.Parse("12345678901234567890") * Integer.One,
        Integer.Parse("12345678901234567890"));

    [Fact]
    public void ZeroAndOne()
    {
        Assert.True(Integer.Zero.IsZero);
        Assert.True(Integer.One.IsOne);
        Assert.False(Integer.One.IsZero);
    }

    // --- Arithmetic ---

    [Fact]
    public void BasicArithmetic()
    {
        Integer a = 7;
        Integer b = 3;
        Assert.Equal((Integer)10, a + b);
        Assert.Equal((Integer)4, a - b);
        Assert.Equal((Integer)21, a * b);
        Assert.Equal((Integer)(-7), -a);
    }

    [Fact]
    public void LargeValues()
    {
        var big = Integer.Parse("100000000000000000000000000000000000000000000000000");
        Assert.Equal(big + Integer.One, Integer.Parse("100000000000000000000000000000000000000000000000001"));
    }

    [Fact]
    public void Powers()
    {
        Integer a = 3;
        Assert.Equal(Integer.One, Integer.Pow(a, 0));
        Assert.Equal(a, Integer.Pow(a, 1));
        Assert.Equal((Integer)27, Integer.Pow(a, 3));
    }

    // --- Division ---

    [Fact]
    public void DivisionAlgorithm()
    {
        Integer a = 7, b = 3;
        var (q, r) = Integer.DivMod(a, b);
        Assert.Equal((Integer)2, q);
        Assert.Equal((Integer)1, r);
        Assert.Equal(a, q * b + r);
    }

    [Fact]
    public void DivisionByZero()
    {
        Integer a = 7;
        Assert.Equal(Integer.Zero, a / Integer.Zero);
        Assert.Equal(Integer.Zero, a % Integer.Zero);
        var (q, r) = Integer.DivMod(a, Integer.Zero);
        Assert.Equal(Integer.Zero, q);
        Assert.Equal(Integer.Zero, r);
    }

    // --- GCD ---

    [Fact]
    public void GcdBasic()
    {
        Assert.Equal((Integer)4, Integer.Gcd((Integer)12, (Integer)8));
        Assert.Equal((Integer)1, Integer.Gcd((Integer)7, (Integer)11));
    }

    [Fact]
    public void GcdWithZero()
    {
        Integer n = 5;
        Assert.Equal(n, Integer.Gcd(n, Integer.Zero));
        Assert.Equal(n, Integer.Gcd(Integer.Zero, n));
    }

    [Fact]
    public void LcmBasic()
    {
        Assert.Equal((Integer)24, Integer.Lcm((Integer)12, (Integer)8));
        Assert.Equal(Integer.Zero, Integer.Lcm(Integer.Zero, (Integer)5));
    }

    // --- Ordered axioms ---

    [Theory]
    [MemberData(nameof(Pairs))]
    public void OrderedReflexivity(Integer a, Integer _) => OrderedAxioms.VerifyReflexivity(a);

    [Theory]
    [MemberData(nameof(Pairs))]
    public void OrderedTotality(Integer a, Integer b) => OrderedAxioms.VerifyTotality(a, b);

    // --- ICharP ---

    [Fact]
    public void CharacteristicIsZero() => Assert.Equal(0, Integer.Characteristic);

    // --- Test data ---

    public static TheoryData<Integer, Integer, Integer> Triples() => new()
    {
        { (Integer)0, (Integer)0, (Integer)0 },
        { (Integer)1, (Integer)2, (Integer)3 },
        { (Integer)(-5), (Integer)7, (Integer)(-3) },
        { (Integer)100, (Integer)(-200), (Integer)300 },
    };

    public static TheoryData<Integer, Integer> Pairs() => new()
    {
        { (Integer)0, (Integer)1 },
        { (Integer)(-5), (Integer)5 },
        { (Integer)42, (Integer)(-42) },
    };
}
