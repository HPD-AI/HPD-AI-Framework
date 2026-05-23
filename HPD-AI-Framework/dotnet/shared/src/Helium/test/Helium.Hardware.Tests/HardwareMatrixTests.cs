using Helium.Hardware;

namespace Helium.Hardware.Tests;

public class HardwareMatrixTests
{
    [Fact]
    public void DoubleMatrix_Multiply()
    {
        var a = DoubleMatrix.FromArray(2, 2, [1.0, 2.0, 3.0, 4.0]);
        var b = DoubleMatrix.FromArray(2, 2, [5.0, 6.0, 7.0, 8.0]);

        var result = Blas.Multiply(a, b);

        Assert.Equal(19.0, result[0, 0]);
        Assert.Equal(22.0, result[0, 1]);
        Assert.Equal(43.0, result[1, 0]);
        Assert.Equal(50.0, result[1, 1]);
    }

    [Fact]
    public void FloatMatrix_Multiply()
    {
        var a = FloatMatrix.FromArray(1, 2, [2.0f, 3.0f]);
        var b = FloatMatrix.FromArray(2, 1, [4.0f, 5.0f]);

        var result = Blas.Multiply(a, b);

        Assert.Equal(23.0f, result[0, 0]);
    }

    [Fact]
    public void DimensionMismatch_Throws()
    {
        var a = DoubleMatrix.FromArray(1, 2, [1.0, 2.0]);
        var b = DoubleMatrix.FromArray(3, 1, [1.0, 2.0, 3.0]);

        Assert.Throws<ArgumentException>(() => Blas.Multiply(a, b));
    }

    [Fact]
    public void DoubleMatrix_ElementwiseOps_UseHardwareBuffers()
    {
        using var a = DoubleMatrix.FromArray(1, 3, [1.0, -2.0, 3.0]);
        using var b = DoubleMatrix.FromArray(1, 3, [4.0, 5.0, -6.0]);

        using var sum = HardwareMatrixOps.Add(a, b);
        using var difference = HardwareMatrixOps.Subtract(a, b);
        using var product = HardwareMatrixOps.Multiply(a, b);
        using var negated = HardwareMatrixOps.Negate(a);
        using var scaled = HardwareMatrixOps.Scale(a, 2.0);

        Assert.Equal([5.0, 3.0, -3.0], sum.Data.ToArray());
        Assert.Equal([-3.0, -7.0, 9.0], difference.Data.ToArray());
        Assert.Equal([4.0, -10.0, -18.0], product.Data.ToArray());
        Assert.Equal([-1.0, 2.0, -3.0], negated.Data.ToArray());
        Assert.Equal([2.0, -4.0, 6.0], scaled.Data.ToArray());
        Assert.Equal([1.0, -2.0, 3.0], a.Data.ToArray());
    }

    [Fact]
    public void FloatMatrix_ElementwiseOps_UseHardwareBuffers()
    {
        using var a = FloatMatrix.FromArray(1, 3, [1.0f, -2.0f, 3.0f]);
        using var b = FloatMatrix.FromArray(1, 3, [4.0f, 5.0f, -6.0f]);

        using var sum = HardwareMatrixOps.Add(a, b);
        using var difference = HardwareMatrixOps.Subtract(a, b);
        using var product = HardwareMatrixOps.Multiply(a, b);
        using var negated = HardwareMatrixOps.Negate(a);
        using var scaled = HardwareMatrixOps.Scale(a, 2.0f);

        Assert.Equal([5.0f, 3.0f, -3.0f], sum.Data.ToArray());
        Assert.Equal([-3.0f, -7.0f, 9.0f], difference.Data.ToArray());
        Assert.Equal([4.0f, -10.0f, -18.0f], product.Data.ToArray());
        Assert.Equal([-1.0f, 2.0f, -3.0f], negated.Data.ToArray());
        Assert.Equal([2.0f, -4.0f, 6.0f], scaled.Data.ToArray());
        Assert.Equal([1.0f, -2.0f, 3.0f], a.Data.ToArray());
    }

    [Fact]
    public void DoubleMatrix_Reductions_UseTensorPrimitives()
    {
        using var a = DoubleMatrix.FromArray(1, 3, [3.0, 4.0, 12.0]);
        using var b = DoubleMatrix.FromArray(1, 3, [2.0, 3.0, 4.0]);

        Assert.Equal(19.0, HardwareMatrixOps.Sum(a));
        Assert.Equal(19.0 / 3.0, HardwareMatrixOps.Mean(a));
        Assert.Equal(66.0, HardwareMatrixOps.Dot(a, b));
        Assert.Equal(13.0, HardwareMatrixOps.Norm(a));
    }

    [Fact]
    public void FloatMatrix_Reductions_UseTensorPrimitives()
    {
        using var a = FloatMatrix.FromArray(1, 3, [3.0f, 4.0f, 12.0f]);
        using var b = FloatMatrix.FromArray(1, 3, [2.0f, 3.0f, 4.0f]);

        Assert.Equal(19.0f, HardwareMatrixOps.Sum(a));
        Assert.Equal(19.0f / 3.0f, HardwareMatrixOps.Mean(a));
        Assert.Equal(66.0f, HardwareMatrixOps.Dot(a, b));
        Assert.Equal(13.0f, HardwareMatrixOps.Norm(a));
    }

    [Fact]
    public void ElementwiseShapeMismatch_Throws()
    {
        using var a = DoubleMatrix.FromArray(1, 2, [1.0, 2.0]);
        using var b = DoubleMatrix.FromArray(2, 1, [1.0, 2.0]);

        Assert.Throws<ArgumentException>(() => HardwareMatrixOps.Add(a, b));
    }

    [Fact]
    public void HardwareBuffer_CopyTo()
    {
        using var buffer = new HardwareBuffer<double>([1.0, 2.0, 3.0]);
        var copy = new double[3];

        buffer.CopyTo(copy);

        Assert.Equal([1.0, 2.0, 3.0], copy);
    }

    [Fact]
    public void DoubleMatrix_EmptyConstructor_AllocatesPinnedWritableResultBuffer()
    {
        using var matrix = new DoubleMatrix(2, 2);
        var data = matrix.Buffer.AsSpan();

        data[0] = 1.0;
        data[3] = 4.0;

        Assert.Equal(1.0, matrix[0, 0]);
        Assert.Equal(4.0, matrix[1, 1]);
    }

    [Fact]
    public void FloatMatrix_EmptyConstructor_AllocatesPinnedWritableResultBuffer()
    {
        using var matrix = new FloatMatrix(1, 2);
        var data = matrix.Buffer.AsSpan();

        data[0] = 3.0f;
        data[1] = 5.0f;

        Assert.Equal(3.0f, matrix[0, 0]);
        Assert.Equal(5.0f, matrix[0, 1]);
    }

    [Fact]
    public void PackedSpan_MutatesBackingBuffer()
    {
        using var matrix = DoubleMatrix.FromArray(2, 2, [1.0, 2.0, 3.0, 4.0]);
        var row = matrix.RowSpan(1);

        row[0] = 30.0;

        Assert.Equal(30.0, matrix[1, 0]);
    }

    [Fact]
    public void Blas_Backend_IsInspectable()
    {
        Assert.True(Enum.IsDefined(Blas.ActiveBackend));
    }

    [Fact]
    public void DoubleMatrix_NativeOnly_EitherComputesOrThrowsExplicitly()
    {
        var a = DoubleMatrix.FromArray(2, 2, [1.0, 2.0, 3.0, 4.0]);
        var b = DoubleMatrix.FromArray(2, 2, [5.0, 6.0, 7.0, 8.0]);

        if (Blas.IsNativeAvailable)
        {
            var result = Blas.MultiplyNativeOnly(a, b);
            Assert.Equal(19.0, result[0, 0]);
            Assert.Equal(22.0, result[0, 1]);
            Assert.Equal(43.0, result[1, 0]);
            Assert.Equal(50.0, result[1, 1]);
        }
        else
        {
            Assert.Throws<PlatformNotSupportedException>(() => Blas.MultiplyNativeOnly(a, b));
        }
    }

    [Fact]
    public void FloatMatrix_NativeOnly_EitherComputesOrThrowsExplicitly()
    {
        var a = FloatMatrix.FromArray(1, 2, [2.0f, 3.0f]);
        var b = FloatMatrix.FromArray(2, 1, [4.0f, 5.0f]);

        if (Blas.IsNativeAvailable)
        {
            var result = Blas.MultiplyNativeOnly(a, b);
            Assert.Equal(23.0f, result[0, 0]);
        }
        else
        {
            Assert.Throws<PlatformNotSupportedException>(() => Blas.MultiplyNativeOnly(a, b));
        }
    }

    [Fact]
    public void Matrix_CopyTo()
    {
        using var matrix = FloatMatrix.FromArray(1, 3, [1.0f, 2.0f, 3.0f]);
        var copy = new float[3];

        matrix.CopyTo(copy);

        Assert.Equal([1.0f, 2.0f, 3.0f], copy);
    }

    [Fact]
    public void DisposedBuffer_Throws()
    {
        var buffer = new HardwareBuffer<int>(4);
        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => buffer.AsSpan());
    }
}
