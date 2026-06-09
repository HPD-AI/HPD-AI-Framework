using HPD.Math.Core;
using HPD.Math.Numerics;

namespace HPD.Math.Tests;

[PadicScope(typeof(P7), typeof(N3), Handles = 8)]
public partial struct PadicScopeExample
{
    partial void Build(ref Scope z)
    {
        var unit = z.Const(10);
        var inverse = unit.Inv;
        var check = inverse * unit;

        z.Return(check);
    }
}

[PadicScope(typeof(P7), typeof(N3), Handles = 1)]
public partial struct TooSmallPadicScopeExample
{
    partial void Build(ref Scope z)
    {
        var left = z.Const(10);
        var right = z.Const(20);

        z.Return(left + right);
    }
}

public sealed class PadicScopeTests
{
    [Fact]
    public void PadicScope_GeneratesScopeLocalResidueSyntax()
    {
        var result = default(PadicScopeExample.Result);

        var status = new PadicScopeExample().Run(ref result);

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(1, result.Residue);
    }

    [Fact]
    public void PadicScope_RecordsHandleCapacityFailureInScopeStatus()
    {
        var result = default(TooSmallPadicScopeExample.Result);

        var status = new TooSmallPadicScopeExample().Run(ref result);

        Assert.Equal(AlgebraStatus.InsufficientDestination, status);
        Assert.Equal(Padic32<P7, N3>.Zero, result.Value);
    }
}
