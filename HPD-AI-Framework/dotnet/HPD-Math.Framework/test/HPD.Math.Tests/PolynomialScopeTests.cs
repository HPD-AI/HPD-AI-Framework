using System;
using HPD.Math.Core;
using HPD.Math.Numerics;

namespace HPD.Math.Tests;

[PolynomialScope(typeof(Rational32), typeof(Rational32StatusFieldOps), Terms = 4, Workspace = 16, Handles = 8)]
public partial struct RationalPolynomialScopeExample
{
    partial void Build(ref Scope q)
    {
        var x = q.Variable();
        var p = x * x + q.Const(3) * x + q.Const(1);
        var derivative = p.Derivative;

        q.Return(derivative);
    }
}

[PolynomialScope(typeof(Rational32), typeof(Rational32StatusFieldOps), Terms = 2, Workspace = 16, Handles = 8)]
public partial struct TooSmallPolynomialScopeExample
{
    partial void Build(ref Scope q)
    {
        var x = q.Variable();
        var p = x * x + q.Const(3) * x + q.Const(1);

        q.Return(p);
    }
}

public sealed class PolynomialScopeTests
{
    [Fact]
    public void PolynomialScope_GeneratesScopeLocalMathSyntax()
    {
        var result = default(RationalPolynomialScopeExample.Result);

        var status = new RationalPolynomialScopeExample().Run(ref result);

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(2, result.TermCount);
        Assert.Equal(0, result.DegreeAt(0));
        Assert.Equal(new Rational32(3, 1), result.CoefficientAt(0));
        Assert.Equal(1, result.DegreeAt(1));
        Assert.Equal(new Rational32(2, 1), result.CoefficientAt(1));
    }

    [Fact]
    public void PolynomialScope_RecordsCapacityFailureInScopeStatus()
    {
        var result = default(TooSmallPolynomialScopeExample.Result);

        var status = new TooSmallPolynomialScopeExample().Run(ref result);

        Assert.Equal(AlgebraStatus.InsufficientDestination, status);
        Assert.Equal(0, result.TermCount);
    }
}
