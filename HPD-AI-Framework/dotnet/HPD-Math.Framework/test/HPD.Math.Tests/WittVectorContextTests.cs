using System;
using HPD.Math.Core;

namespace HPD.Math.Tests;

[WittVectorContext(typeof(int), typeof(CheckedInt32RingOps), typeof(P2), 2)]
public readonly partial struct WittVectorContextExample;

[WittVectorContext(typeof(int), typeof(CheckedInt32RingOps), typeof(P2), 3)]
public readonly partial struct LengthThreeWittVectorContextExample;

public sealed class WittVectorContextTests
{
    [Fact]
    public void WittVectorContext_GeneratesFirstClassInlineVectorValue()
    {
        var ops = default(WittVectorContextExample.Ops);

        var status = WittVectorContextExample.TryFromComponents([1, 2], out var left);
        Assert.Equal(AlgebraStatus.Ok, status);

        status = WittVectorContextExample.TryFromComponents([3, 4], out var right);
        Assert.Equal(AlgebraStatus.Ok, status);

        status = ops.TryMul(left, right, out var result);
        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(2, result.ComponentCount);
        Assert.Equal(3, result[0]);
        Assert.Equal(38, result[1]);

        var ghost = 0;
        status = ops.TryGhostComponent(result, 1, ref ghost);
        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(3 * 3 + 2 * 38, ghost);
    }

    [Fact]
    public void WittVectorContext_RejectsWrongComponentCount()
    {
        var status = WittVectorContextExample.TryFromComponents([1], out var result);

        Assert.Equal(AlgebraStatus.DimensionMismatch, status);
        Assert.Equal(default, result);
    }

    [Fact]
    public void WittVectorContext_DoesNotTurnArithmeticLimitIntoUniverseLimit()
    {
        var ops = default(LengthThreeWittVectorContextExample.Ops);

        var status = LengthThreeWittVectorContextExample.TryFromComponents([1, 2, 3], out var left);
        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(3, left.ComponentCount);
        Assert.Equal(3, left[2]);

        status = LengthThreeWittVectorContextExample.TryFromComponents([4, 5, 6], out var right);
        Assert.Equal(AlgebraStatus.Ok, status);

        var ghost = 0;
        status = ops.TryGhostComponent(left, 2, ref ghost);
        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(21, ghost);

        status = ops.TryAdd(left, right, out var sum);
        Assert.Equal(AlgebraStatus.InvalidInput, status);
        Assert.Equal(default, sum);
    }
}
