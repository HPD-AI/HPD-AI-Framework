using System;
using HPD.Math.Core;
using HPD.Math.Numerics;

namespace HPD.Math.Tests;

[ReverseDiffScope(typeof(Rational32), typeof(Rational32StatusFieldOps), Nodes = 8)]
public partial struct RationalReverseDiffScopeExample
{
    partial void Build(ref Scope d)
    {
        var x = d.Input(new Rational32(2, 1));
        var y = d.Input(new Rational32(5, 1));
        var output = (x * y) + x;

        d.Return(output);
    }
}

[ReverseDiffScope(typeof(Rational32), typeof(Rational32StatusFieldOps), Nodes = 2)]
public partial struct TooSmallReverseDiffScopeExample
{
    partial void Build(ref Scope d)
    {
        var x = d.Input(new Rational32(2, 1));
        var y = d.Const(3);

        d.Return(x * y);
    }
}

public sealed class ReverseDiffScopeTests
{
    [Fact]
    public void ReverseDiffScope_GeneratesScopeLocalReverseModeSyntax()
    {
        var result = default(RationalReverseDiffScopeExample.Result);

        var status = new RationalReverseDiffScopeExample().Run(ref result);

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(new Rational32(12, 1), result.Primal);
        Assert.Equal(4, result.GradientCount);
        Assert.Equal(new Rational32(6, 1), result.GradientAt(0));
        Assert.Equal(new Rational32(2, 1), result.GradientAt(1));
    }

    [Fact]
    public void ReverseDiffScope_RecordsNodeCapacityFailureInScopeStatus()
    {
        var result = default(TooSmallReverseDiffScopeExample.Result);

        var status = new TooSmallReverseDiffScopeExample().Run(ref result);

        Assert.Equal(AlgebraStatus.InsufficientDestination, status);
        Assert.Equal(Rational32.Zero, result.Primal);
        Assert.Equal(0, result.GradientCount);
    }
}
