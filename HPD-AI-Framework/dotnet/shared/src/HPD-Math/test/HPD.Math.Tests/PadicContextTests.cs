using System;
using HPD.Math.Core;
using HPD.Math.Numerics;

namespace HPD.Math.Tests;

[PadicContext(typeof(P7), typeof(N3), Handles = 8)]
public readonly partial struct PadicContextExample;

public sealed class PadicContextTests
{
    [Fact]
    public void PadicContext_GeneratesFirstClassValues()
    {
        var z = PadicContextExample.CreateOps();

        var status = z.TryConst(10, out var unit);
        Assert.Equal(AlgebraStatus.Ok, status);

        status = z.TryInv(unit, out var inverse);
        Assert.Equal(AlgebraStatus.Ok, status);

        status = z.TryMul(inverse, unit, out var value);
        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(1, value.Residue);
    }
}
