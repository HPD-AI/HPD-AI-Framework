using HPD.Math.Core;
using HPD.Math.Numerics;

namespace HPD.Math.Tests;

[SparsePolynomialContext(typeof(Rational32), typeof(Rational32StatusFieldOps), Terms = 4)]
public readonly partial struct RationalSparsePolynomialContext;

public sealed class SparsePolynomialContextTests
{
    [Fact]
    public void SparsePolynomialContext_GeneratesFirstClassInlineSparsePolyValue()
    {
        var ops = default(RationalSparsePolynomialContext.Ops);

        var status = RationalSparsePolynomialContext.TryVariable(out var x);
        Assert.Equal(AlgebraStatus.Ok, status);
        status = RationalSparsePolynomialContext.TryConst(3, out var three);
        Assert.Equal(AlgebraStatus.Ok, status);

        status = ops.TryMul(x, x, out var xSquared);
        Assert.Equal(AlgebraStatus.Ok, status);
        status = ops.TryMul(three, x, out var threeX);
        Assert.Equal(AlgebraStatus.Ok, status);
        status = ops.TryAdd(xSquared, threeX, out var polynomial);
        Assert.Equal(AlgebraStatus.Ok, status);
        status = ops.TryDerivative(polynomial, out var derivative);

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(2, derivative.TermCount);
        Assert.Equal(0, derivative.DegreeAt(0));
        Assert.Equal(new Rational32(3, 1), derivative.CoefficientAt(0));
        Assert.Equal(1, derivative.DegreeAt(1));
        Assert.Equal(new Rational32(2, 1), derivative.CoefficientAt(1));
    }

    [Fact]
    public void SparsePolynomialContext_RejectsNonCanonicalTerms()
    {
        var status = RationalSparsePolynomialContext.TryFromTerms(
            [2, 1],
            [new Rational32(1, 1), new Rational32(1, 1)],
            out var result);

        Assert.Equal(AlgebraStatus.InvalidInput, status);
        Assert.Equal(default, result);
    }
}
