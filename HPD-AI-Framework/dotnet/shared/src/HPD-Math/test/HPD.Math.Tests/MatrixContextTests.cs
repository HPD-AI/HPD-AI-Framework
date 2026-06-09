using System;
using HPD.Math.Core;

namespace HPD.Math.Tests;

[MatrixContext(typeof(int), typeof(CheckedInt32RingOps), Rows = 2, Columns = 2, Handles = 8)]
public readonly partial struct IntMatrixContext;

public sealed class MatrixContextTests
{
    [Fact]
    public void MatrixContext_GeneratesFirstClassInlineMatrixValue()
    {
        var ops = default(IntMatrixContext.Ops);

        var status = IntMatrixContext.TryFromValues([1, 2, 3, 4], out var a);
        Assert.Equal(AlgebraStatus.Ok, status);

        status = ops.TryIdentity(out var identity);
        Assert.Equal(AlgebraStatus.Ok, status);

        status = ops.TryAdd(a, identity, out var sum);
        Assert.Equal(AlgebraStatus.Ok, status);

        status = ops.TryTranspose(identity, out var transposedIdentity);
        Assert.Equal(AlgebraStatus.Ok, status);

        status = ops.TryMul(sum, transposedIdentity, out var result);
        Assert.Equal(AlgebraStatus.Ok, status);

        Assert.Equal(2, result.RowCount);
        Assert.Equal(2, result.ColumnCount);
        Assert.Equal(2, result[0, 0]);
        Assert.Equal(2, result[0, 1]);
        Assert.Equal(3, result[1, 0]);
        Assert.Equal(5, result[1, 1]);
    }

    [Fact]
    public void MatrixContext_RejectsInvalidInlineValueInput()
    {
        var status = IntMatrixContext.TryFromValues([1, 2, 3], out var result);

        Assert.Equal(AlgebraStatus.InvalidInput, status);
        Assert.Equal(default, result);
    }
}
