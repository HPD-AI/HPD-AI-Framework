using Helium.Algebra;
using Helium.Primitives;

namespace Helium.Algebra.Tests;

public class FixedVectorTests
{
    [Fact]
    public void FixedVector_SatisfiesModuleLaws()
    {
        var v = FixedVector<Integer, Dim3>.FromArray((Integer)1, (Integer)(-2), (Integer)3);
        var w = FixedVector<Integer, Dim3>.FromArray((Integer)4, (Integer)5, (Integer)(-6));
        var a = (Integer)2;
        var b = (Integer)(-3);

        Assert.Equal(v, v + FixedVector<Integer, Dim3>.Zero);
        Assert.Equal(v, FixedVector<Integer, Dim3>.Zero + v);
        Assert.Equal(FixedVector<Integer, Dim3>.Zero, v + (-v));
        Assert.Equal(v + w, w + v);
        Assert.Equal(v, FixedVector<Integer, Dim3>.ScalarMultiply(Integer.One, v));
        Assert.Equal(
            FixedVector<Integer, Dim3>.ScalarMultiply(a * b, v),
            FixedVector<Integer, Dim3>.ScalarMultiply(a, FixedVector<Integer, Dim3>.ScalarMultiply(b, v)));
        Assert.Equal(
            FixedVector<Integer, Dim3>.ScalarMultiply(a, v + w),
            FixedVector<Integer, Dim3>.ScalarMultiply(a, v) + FixedVector<Integer, Dim3>.ScalarMultiply(a, w));
        Assert.Equal(
            FixedVector<Integer, Dim3>.ScalarMultiply(a + b, v),
            FixedVector<Integer, Dim3>.ScalarMultiply(a, v) + FixedVector<Integer, Dim3>.ScalarMultiply(b, v));
        Assert.Equal(FixedVector<Integer, Dim3>.Zero, FixedVector<Integer, Dim3>.ScalarMultiply(Integer.Zero, v));
    }

    [Fact]
    public void FixedVector_RejectsWrongDimension()
    {
        Assert.Throws<ArgumentException>(() =>
            FixedVector<Integer, Dim3>.FromArray((Integer)1, (Integer)2));
    }

    private readonly struct Dim3 : IFiniteDimension
    {
        public static int Value => 3;
    }
}
