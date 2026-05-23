using Helium.Algebra;
using Helium.Hardware;
using Helium.Primitives;

namespace Helium.Hardware.Tests;

public class ConversionTests
{
    [Fact]
    public void ExactRationalMatrix_ApproximateToFloat64_IsExplicitAndLossy()
    {
        var source = Matrix<Rational>.FromArray(
            2,
            2,
            [(Rational)1, Rational.Create((Integer)1, (Integer)2), (Rational)(-3), Rational.Create((Integer)7, (Integer)4)]);

        using var result = HardwareConvert.ApproximateToFloat64(source);

        Assert.Equal(2, result.Rows);
        Assert.Equal(2, result.Cols);
        Assert.Equal(1.0, result[0, 0]);
        Assert.Equal(0.5, result[0, 1]);
        Assert.Equal(-3.0, result[1, 0]);
        Assert.Equal(1.75, result[1, 1]);
    }

    [Fact]
    public void ExactIntegerMatrix_ApproximateToFloat32_IsExplicitAndLossy()
    {
        var source = Matrix<Integer>.FromArray(1, 3, [(Integer)1, (Integer)(-2), (Integer)3]);

        using var result = HardwareConvert.ApproximateToFloat32(source);

        Assert.Equal(1.0f, result[0, 0]);
        Assert.Equal(-2.0f, result[0, 1]);
        Assert.Equal(3.0f, result[0, 2]);
    }

    [Fact]
    public void DensePolynomial_ApproximateToCoefficientBuffer_PreservesCoefficientOrder()
    {
        var source = DensePolynomial<Rational>.FromCoeffs(
            (Rational)3,
            Rational.Create((Integer)1, (Integer)2),
            (Rational)(-4));

        using var buffer = HardwareConvert.ApproximateToFloat64CoefficientBuffer(source);
        var copy = new double[buffer.Length];
        buffer.CopyTo(copy);

        Assert.Equal([3.0, 0.5, -4.0], copy);
    }

    [Fact]
    public void ExactRationalMatrix_ApproximateToBackendTensor_CreatesExplicitHardwareTensor()
    {
        var backend = new LocalHardwareBackend<float>();
        var source = Matrix<Rational>.FromArray(
            2,
            2,
            [(Rational)1, Rational.Create((Integer)1, (Integer)2), (Rational)(-3), Rational.Create((Integer)7, (Integer)4)]);

        using var result = HardwareConvert.ApproximateToFloat32Tensor(backend, source);

        var copy = new float[4];
        result.CopyToHost(copy);
        Assert.Equal(2, result.Rows);
        Assert.Equal(2, result.Cols);
        Assert.Equal([1.0f, 0.5f, -3.0f, 1.75f], copy);
    }

    [Fact]
    public void DoubleMatrix_ToIntervals_RequiresExplicitRadius()
    {
        using var source = DoubleMatrix.FromArray(1, 2, [10.0, -5.0]);

        var result = ValidatedConvert.ToIntervals(source, 0.25);

        Assert.Equal(1, result.Rows);
        Assert.Equal(2, result.Cols);
        Assert.True(result[0, 0].Contains(10.0));
        Assert.True(result[0, 0].Contains(9.75));
        Assert.True(result[0, 0].Contains(10.25));
        Assert.False(result[0, 0].Contains(10.5));
        Assert.True(result[0, 1].Contains(-5.0));
    }

    [Fact]
    public void ToIntervals_NegativeRadius_Throws()
    {
        using var source = FloatMatrix.FromArray(1, 1, [1.0f]);

        Assert.Throws<ArgumentOutOfRangeException>(() => ValidatedConvert.ToIntervals(source, -0.1));
    }
}
