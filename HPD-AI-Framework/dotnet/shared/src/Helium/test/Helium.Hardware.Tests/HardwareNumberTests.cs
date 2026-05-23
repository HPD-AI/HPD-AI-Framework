using Helium.Hardware;
using Helium.Primitives;

namespace Helium.Hardware.Tests;

public class HardwareNumberTests
{
    [Fact]
    public void Int4_StoresSignedNibble()
    {
        var seven = new Int4(7);
        var minusEight = new Int4(8);
        var minusOne = Int4.FromRawNibble(0x0F);

        Assert.Equal(7, seven.Value);
        Assert.Equal(-8, minusEight.Value);
        Assert.Equal(-1, minusOne.Value);
        Assert.Equal(0x0F, minusOne.RawNibble);
    }

    [Fact]
    public void Int4_ArithmeticWrapsToFourBits()
    {
        var result = new Int4(7) + new Int4(1);

        Assert.Equal(-8, result.Value);
        Assert.Equal(0x08, result.RawNibble);
    }

    [Fact]
    public void Int8_ArithmeticWrapsToEightBits()
    {
        var result = new Int8(127) + new Int8(1);

        Assert.Equal(-128, result.Value);
        Assert.Equal(0x80, result.RawByte);
    }

    [Fact]
    public void Fix32_RoundTripsRawBitsAndMultiplies()
    {
        var a = Fix32.FromDouble(1.5);
        var b = Fix32.FromDouble(2.0);
        var product = a * b;

        Assert.Equal(Fix32.OneRaw + (Fix32.OneRaw >> 1), a.RawBits);
        Assert.Equal(3.0, product.ToDouble(), 1e-6);
        Assert.Equal(a, Fix32.FromRawBits(a.RawBits));
    }

    [Fact]
    public void Fix64_RoundTripsRawBitsAndMultiplies()
    {
        var a = Fix64.FromDouble(1.5);
        var b = Fix64.FromDouble(2.0);
        var product = a * b;

        Assert.Equal(Fix64.OneRaw + (Fix64.OneRaw >> 1), a.RawBits);
        Assert.Equal(3.0, product.ToDouble(), 1e-12);
        Assert.Equal(a, Fix64.FromRawBits(a.RawBits));
    }

    [Fact]
    public void HardwareLaneTypes_DoNotImplementExactAlgebraInterfaces()
    {
        Type[] hardwareLaneTypes =
        [
            typeof(Int4),
            typeof(Int8),
            typeof(Fix32),
            typeof(Fix64),
            typeof(UInt128),
            typeof(Int128),
            typeof(UInt256),
            typeof(Int256),
            typeof(UInt4096),
            typeof(Int4096)
        ];

        var exactInterfaces = new[]
        {
            typeof(ISemiring<>),
            typeof(IRing<>),
            typeof(ICommRing<>),
            typeof(IField<>),
            typeof(IGcdDomain<>),
            typeof(IEuclideanDomain<>)
        };

        foreach (var type in hardwareLaneTypes)
        {
            Assert.DoesNotContain(type.GetInterfaces(), i =>
                i.IsGenericType && exactInterfaces.Contains(i.GetGenericTypeDefinition()));
        }
    }

    [Fact]
    public void GoldilocksElement_ReducesModuloFieldPrime()
    {
        var value = new GoldilocksElement(GoldilocksElement.Modulus);

        Assert.Equal(0UL, value.Value);
    }

    [Fact]
    public void UInt128_UsesRawLaneWraparound()
    {
        var max = UInt128.FromSystem(System.UInt128.MaxValue);
        var wrapped = max + UInt128.One;

        Assert.Equal(UInt128.Zero, wrapped);
        Assert.Equal(ulong.MaxValue, max.Lo);
        Assert.Equal(ulong.MaxValue, max.Hi);
    }

    [Fact]
    public void Int128_UsesRawLaneWraparound()
    {
        var max = Int128.FromSystem(System.Int128.MaxValue);
        var wrapped = max + Int128.One;

        Assert.Equal(System.Int128.MinValue, wrapped.ToSystem());
    }

    [Fact]
    public void UInt256_AdditionWrapsAcrossFourLimbs()
    {
        var max = new UInt256(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue);
        var wrapped = max + UInt256.One;

        Assert.Equal(UInt256.Zero, wrapped);
    }

    [Fact]
    public void UInt256_SubtractionBorrowsAcrossFourLimbs()
    {
        var wrapped = UInt256.Zero - UInt256.One;

        Assert.Equal(new UInt256(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue), wrapped);
    }

    [Fact]
    public void UInt256_MultiplyKeepsLowTwoHundredFiftySixBits()
    {
        var a = new UInt256(ulong.MaxValue);
        var b = new UInt256(2);
        var product = a * b;

        Assert.Equal(ulong.MaxValue - 1, product.L0);
        Assert.Equal(1UL, product.L1);
        Assert.Equal(0UL, product.L2);
        Assert.Equal(0UL, product.L3);
    }

    [Fact]
    public void Int256_UsesTwosComplementWraparoundAndSignedComparison()
    {
        var minusOne = new Int256(-1);
        var zero = Int256.Zero;
        var min = new Int256(new UInt256(0, 0, 0, 1UL << 63));
        var max = min - Int256.One;

        Assert.True(min.IsNegative);
        Assert.True(min < minusOne);
        Assert.True(min < zero);
        Assert.True(max > zero);
        Assert.Equal(Int256.Zero, minusOne + Int256.One);
    }

    [Fact]
    public void UInt4096_AdditionWrapsAcrossSixtyFourLimbs()
    {
        Span<ulong> limbs = stackalloc ulong[UInt4096.LimbCount];
        limbs.Fill(ulong.MaxValue);
        var max = new UInt4096(limbs);

        var wrapped = max + UInt4096.One;

        Assert.Equal(UInt4096.Zero, wrapped);
    }

    [Fact]
    public void UInt4096_SubtractionBorrowsAcrossSixtyFourLimbs()
    {
        var wrapped = UInt4096.Zero - UInt4096.One;

        for (var i = 0; i < UInt4096.LimbCount; i++)
            Assert.Equal(ulong.MaxValue, wrapped[i]);
    }

    [Fact]
    public void UInt4096_MultiplyKeepsLowFourThousandNinetySixBits()
    {
        Span<ulong> high = stackalloc ulong[UInt4096.LimbCount];
        high[32] = 1;
        var a = new UInt4096(high);
        var product = a * a;

        Assert.Equal(0UL, product[0]);
        Assert.Equal(0UL, product[63]);
        Assert.Equal(UInt4096.Zero, product);

        var low = new UInt4096(ulong.MaxValue);
        var doubled = low * new UInt4096(2);
        Assert.Equal(ulong.MaxValue - 1, doubled[0]);
        Assert.Equal(1UL, doubled[1]);
    }

    [Fact]
    public void Int4096_UsesTwosComplementWraparoundAndSignedComparison()
    {
        var minusOne = new Int4096(-1);
        var zero = Int4096.Zero;
        Span<ulong> minLimbs = stackalloc ulong[UInt4096.LimbCount];
        minLimbs[UInt4096.LimbCount - 1] = 1UL << 63;
        var min = new Int4096(new UInt4096(minLimbs));
        var max = min - Int4096.One;

        Assert.True(min.IsNegative);
        Assert.True(min < minusOne);
        Assert.True(min < zero);
        Assert.True(max > zero);
        Assert.Equal(Int4096.Zero, minusOne + Int4096.One);
    }

    [Fact]
    public void GoldilocksElement_FieldArithmetic()
    {
        var a = new GoldilocksElement(GoldilocksElement.Modulus - 1);
        var b = new GoldilocksElement(2);
        var sum = a + b;
        var product = new GoldilocksElement(3) * new GoldilocksElement(5);

        Assert.Equal(1UL, sum.Value);
        Assert.Equal(15UL, product.Value);
    }

    [Fact]
    public void GoldilocksElement_Invert_ReturnsMultiplicativeInverse()
    {
        var value = new GoldilocksElement(123456789UL);
        var inverse = GoldilocksElement.Invert(value);

        Assert.Equal(GoldilocksElement.MultiplicativeIdentity, value * inverse);
    }

    [Fact]
    public void GoldilocksElement_IsExactFieldDomain()
    {
        Assert.Contains(typeof(GoldilocksElement).GetInterfaces(), i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IField<>));
    }

    [Fact]
    public void BinaryGcd_Ulong_UsesSteinAlgorithm()
    {
        Assert.Equal(6UL, BinaryGcd.Compute(270, 192));
        Assert.Equal(17UL, BinaryGcd.Compute(0, 17));
        Assert.Equal(17UL, BinaryGcd.Compute(17, 0));
    }

    [Fact]
    public void BinaryGcd_UInt256_ComputesAcrossLimbs()
    {
        var a = new UInt256(0, 12); // 12 * 2^64
        var b = new UInt256(0, 18); // 18 * 2^64

        var gcd = BinaryGcd.Compute(a, b);

        Assert.Equal(new UInt256(0, 6), gcd);
    }
}
