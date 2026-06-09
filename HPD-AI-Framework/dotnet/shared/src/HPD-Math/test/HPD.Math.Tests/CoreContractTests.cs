using System;
using HPD.Math.Core;

namespace HPD.Math.Tests;

public sealed class CoreContractTests
{
    [Fact]
    public void BoolAlgebraOps_EnumeratesFiniteBooleanAlgebra()
    {
        var ops = new BoolAlgebraOps();
        Span<bool> values = stackalloc bool[2];

        Assert.Equal(AlgebraStatus.Ok, ops.TryFill(values));
        Assert.False(values[0]);
        Assert.True(values[1]);
        Assert.Equal(2, ops.Cardinality);
    }

    [Fact]
    public void BoolAlgebraOps_ComputesLatticeOperations()
    {
        var ops = new BoolAlgebraOps();
        var result = false;

        ops.Join(ref result, false, true);
        Assert.True(result);

        ops.Meet(ref result, true, false);
        Assert.False(result);

        ops.Complement(ref result, false);
        Assert.True(result);

        Assert.True(ops.LessEqual(false, true));
        Assert.False(ops.LessEqual(true, false));
    }

    [Fact]
    public void BoolAlgebraOps_ComputesCompleteFiniteFolds()
    {
        var ops = new BoolAlgebraOps();
        var result = false;

        ReadOnlySpan<bool> values = [false, true, false];

        Assert.Equal(AlgebraStatus.Ok, ops.TrySupremum(ref result, values));
        Assert.True(result);

        Assert.Equal(AlgebraStatus.Ok, ops.TryInfimum(ref result, values));
        Assert.False(result);

        Assert.Equal(AlgebraStatus.Ok, ops.TrySupremum(ref result, []));
        Assert.False(result);

        Assert.Equal(AlgebraStatus.Ok, ops.TryInfimum(ref result, []));
        Assert.True(result);
    }
}
