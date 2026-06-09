using System;
using HPD.Math.Core;

namespace HPD.Math.Tests;

[WittVectorScope(typeof(int), typeof(CheckedInt32RingOps), typeof(P2), typeof(N2), Handles = 8)]
public partial struct WittVectorScopeExample
{
    partial void Build(ref Scope w)
    {
        var left = w.FromComponents([1, 2]);
        var right = w.FromComponents([3, 4]);

        w.Return(left * right);
    }
}

[WittVectorScope(typeof(int), typeof(CheckedInt32RingOps), typeof(P2), typeof(N2), Handles = 1)]
public partial struct TooSmallWittVectorScopeExample
{
    partial void Build(ref Scope w)
    {
        var left = w.FromComponents([1, 2]);
        var right = w.FromComponents([3, 4]);

        w.Return(left + right);
    }
}

public sealed class WittVectorScopeTests
{
    [Fact]
    public void WittVectorScope_GeneratesScopeLocalComponentSyntax()
    {
        var result = default(WittVectorScopeExample.Result);

        var status = new WittVectorScopeExample().Run(ref result);

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(2, result.ComponentCount);
        Assert.Equal(3, result.ComponentAt(0));
        Assert.Equal(38, result.ComponentAt(1));
    }

    [Fact]
    public void WittVectorScope_RecordsHandleCapacityFailureInScopeStatus()
    {
        var result = default(TooSmallWittVectorScopeExample.Result);

        var status = new TooSmallWittVectorScopeExample().Run(ref result);

        Assert.Equal(AlgebraStatus.InsufficientDestination, status);
        Assert.Equal(0, result.ComponentCount);
    }
}
