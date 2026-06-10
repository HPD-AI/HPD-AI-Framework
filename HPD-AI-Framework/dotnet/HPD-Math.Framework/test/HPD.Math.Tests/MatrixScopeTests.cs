using System;
using HPD.Math.Core;

namespace HPD.Math.Tests;

[MatrixScope(typeof(int), typeof(CheckedInt32RingOps), Rows = 2, Columns = 2, Handles = 8)]
public partial struct IntMatrixScopeExample
{
    partial void Build(ref Scope m)
    {
        var a = m.FromValues([1, 2, 3, 4]);
        var i = m.Identity();
        var result = (a + i) * i.Transpose;

        m.Return(result);
    }
}

[MatrixScope(typeof(int), typeof(CheckedInt32RingOps), Rows = 2, Columns = 2, Handles = 2)]
public partial struct TooSmallMatrixScopeExample
{
    partial void Build(ref Scope m)
    {
        var a = m.FromValues([1, 2, 3, 4]);
        var b = m.Identity();

        m.Return(a + b);
    }
}

public sealed class MatrixScopeTests
{
    [Fact]
    public void MatrixScope_GeneratesScopeLocalMatrixSyntax()
    {
        var result = default(IntMatrixScopeExample.Result);

        var status = new IntMatrixScopeExample().Run(ref result);

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(2, result.RowCount);
        Assert.Equal(2, result.ColumnCount);
        Assert.Equal(2, result[0, 0]);
        Assert.Equal(2, result[0, 1]);
        Assert.Equal(3, result[1, 0]);
        Assert.Equal(5, result[1, 1]);
    }

    [Fact]
    public void MatrixScope_RecordsHandleCapacityFailureInScopeStatus()
    {
        var result = default(TooSmallMatrixScopeExample.Result);

        var status = new TooSmallMatrixScopeExample().Run(ref result);

        Assert.Equal(AlgebraStatus.InsufficientDestination, status);
        Assert.Equal(0, result.RowCount);
        Assert.Equal(0, result.ColumnCount);
    }
}
