using System;
using HPD.Math.Core;
using HPD.Math.LinearAlgebra;

namespace HPD.Math.Tests;

public sealed class LinearAlgebraKernelTests
{
    [Fact]
    public void VectorKernels_AddAndDot()
    {
        ReadOnlySpan<int> left = [1, 2, 3];
        ReadOnlySpan<int> right = [4, 5, 6];
        Span<int> output = stackalloc int[3];

        var destination = new VectorBuilder<int>(output);
        var addStatus = VectorKernels.TryAdd(
            new VectorView<int>(left),
            new VectorView<int>(right),
            ref destination,
            new CheckedInt32RingOps());

        Assert.Equal(AlgebraStatus.Ok, addStatus);
        Assert.True(destination.WrittenSpan.SequenceEqual([5, 7, 9]));

        var dot = 0;
        var dotStatus = VectorKernels.TryDot(
            new VectorView<int>(left),
            new VectorView<int>(right),
            ref dot,
            new CheckedInt32RingOps());

        Assert.Equal(AlgebraStatus.Ok, dotStatus);
        Assert.Equal(32, dot);
    }

    [Fact]
    public void MatrixKernels_TransposeAndMultiply()
    {
        ReadOnlySpan<int> leftValues = [1, 2, 3, 4, 5, 6];
        ReadOnlySpan<int> rightValues = [7, 8, 9, 10, 11, 12];
        Span<int> transposeValues = stackalloc int[6];
        Span<int> multiplyValues = stackalloc int[4];

        var left = new MatrixView<int>(2, 3, leftValues);
        var right = new MatrixView<int>(3, 2, rightValues);

        var transpose = new MatrixBuilder<int>(transposeValues);
        var transposeStatus = MatrixKernels.TryTranspose(left, ref transpose);

        Assert.Equal(AlgebraStatus.Ok, transposeStatus);
        Assert.Equal(3, transpose.Rows);
        Assert.Equal(2, transpose.Columns);
        Assert.True(transpose.WrittenSpan.SequenceEqual([1, 4, 2, 5, 3, 6]));

        var product = new MatrixBuilder<int>(multiplyValues);
        var multiplyStatus = MatrixKernels.TryMul(left, right, ref product, new CheckedInt32RingOps());

        Assert.Equal(AlgebraStatus.Ok, multiplyStatus);
        Assert.Equal(2, product.Rows);
        Assert.Equal(2, product.Columns);
        Assert.True(product.WrittenSpan.SequenceEqual([58, 64, 139, 154]));
    }

    [Fact]
    public void MatrixKernels_MultiplyVector()
    {
        ReadOnlySpan<int> matrixValues = [1, 2, 3, 4, 5, 6];
        ReadOnlySpan<int> vectorValues = [7, 8, 9];
        Span<int> outputValues = stackalloc int[2];

        var matrix = new MatrixView<int>(2, 3, matrixValues);
        var vector = new VectorView<int>(vectorValues);
        var destination = new VectorBuilder<int>(outputValues);

        var status = MatrixKernels.TryMulVector(matrix, vector, ref destination, new CheckedInt32RingOps());

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.True(destination.WrittenSpan.SequenceEqual([50, 122]));
    }
}
