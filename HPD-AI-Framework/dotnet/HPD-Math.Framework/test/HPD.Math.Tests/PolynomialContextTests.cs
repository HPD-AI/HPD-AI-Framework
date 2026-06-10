using System;
using HPD.Math.Core;
using HPD.Math.Numerics;

namespace HPD.Math.Tests;

[PolynomialContext(typeof(Rational32), typeof(Rational32StatusFieldOps), Terms = 4, Workspace = 16, Handles = 8)]
public readonly partial struct RationalPolynomialContext;

public sealed class PolynomialContextTests
{
    [Fact]
    public void PolynomialContext_GeneratesFirstClassDenseInlinePolyValue()
    {
        var ops = default(RationalPolynomialContext.Ops);

        var status = RationalPolynomialContext.TryVariable(out var x);
        Assert.Equal(AlgebraStatus.Ok, status);
        status = RationalPolynomialContext.TryConst(3, out var three);
        Assert.Equal(AlgebraStatus.Ok, status);
        status = RationalPolynomialContext.TryConst(1, out var one);
        Assert.Equal(AlgebraStatus.Ok, status);

        status = ops.TryMul(x, x, out var xSquared);
        Assert.Equal(AlgebraStatus.Ok, status);
        status = ops.TryMul(three, x, out var threeX);
        Assert.Equal(AlgebraStatus.Ok, status);
        status = ops.TryAdd(xSquared, threeX, out var partial);
        Assert.Equal(AlgebraStatus.Ok, status);
        status = ops.TryAdd(partial, one, out var polynomial);
        Assert.Equal(AlgebraStatus.Ok, status);
        status = ops.TryDerivative(polynomial, out var derivative);

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(2, derivative.CoefficientCount);
        Assert.Equal(1, derivative.Degree);
        Assert.Equal(new Rational32(3, 1), derivative.CoefficientAt(0));
        Assert.Equal(new Rational32(2, 1), derivative.CoefficientAt(1));
    }

    [Fact]
    public void PolynomialContext_RejectsOverCapacityDenseResults()
    {
        var status = RationalPolynomialContext.TryMonomial(4, new Rational32(1, 1), out var result);

        Assert.Equal(AlgebraStatus.InsufficientDestination, status);
        Assert.Equal(default, result);
    }
}
