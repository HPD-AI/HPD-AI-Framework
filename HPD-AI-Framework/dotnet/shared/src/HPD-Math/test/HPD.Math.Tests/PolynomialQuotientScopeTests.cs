using System;
using HPD.Math.Core;

namespace HPD.Math.Tests;

[PolynomialQuotientScope(typeof(int), typeof(GeneratedMod7FieldOps), Terms = 3, Handles = 8, Workspace = 4)]
public partial struct Mod7PolynomialQuotientScopeExample
{
    partial void Build(ref Scope q)
    {
        q.SetModulus([0, 2], [1, 1]);

        var x = q.Generator();
        var result = x * x;

        q.Return(result);
    }
}

[PolynomialQuotientScope(typeof(int), typeof(GeneratedMod7FieldOps), Terms = 3, Handles = 1, Workspace = 4)]
public partial struct TooSmallPolynomialQuotientScopeExample
{
    partial void Build(ref Scope q)
    {
        q.SetModulus([0, 2], [1, 1]);

        var x = q.Generator();
        q.Return(x * x);
    }
}

public sealed class PolynomialQuotientScopeTests
{
    [Fact]
    public void PolynomialQuotientScope_GeneratesScopeLocalQuotientSyntax()
    {
        var result = default(Mod7PolynomialQuotientScopeExample.Result);

        var status = new Mod7PolynomialQuotientScopeExample().Run(ref result);

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(1, result.TermCount);
        Assert.Equal(0, result.DegreeAt(0));
        Assert.Equal(6, result.CoefficientAt(0));
    }

    [Fact]
    public void PolynomialQuotientScope_RecordsHandleCapacityFailureInScopeStatus()
    {
        var result = default(TooSmallPolynomialQuotientScopeExample.Result);

        var status = new TooSmallPolynomialQuotientScopeExample().Run(ref result);

        Assert.Equal(AlgebraStatus.InsufficientDestination, status);
        Assert.Equal(0, result.TermCount);
    }
}
