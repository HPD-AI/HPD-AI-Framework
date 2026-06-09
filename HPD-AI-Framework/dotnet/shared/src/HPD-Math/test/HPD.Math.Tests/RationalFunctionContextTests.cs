using System;
using HPD.Math.Core;

namespace HPD.Math.Tests;

[RationalFunctionContext(typeof(int), typeof(GeneratedMod7FieldOps), Terms = 3, Handles = 4, Workspace = 4)]
public readonly partial struct Mod7RationalFunctionContextExample;

public sealed class RationalFunctionContextTests
{
    [Fact]
    public void RationalFunctionContext_GeneratesFirstClassValues()
    {
        var r = Mod7RationalFunctionContextExample.CreateOps();
        var status = r.TryFromPolynomials(
            [0, 2],
            [6, 1],
            [0, 1],
            [6, 1],
            out var value);
        Assert.Equal(AlgebraStatus.Ok, status);

        status = r.TryNormalize(value, out var result);

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(2, result.NumeratorTermCount);
        Assert.Equal(0, result.NumeratorDegreeAt(0));
        Assert.Equal(1, result.NumeratorCoefficientAt(0));
        Assert.Equal(1, result.NumeratorDegreeAt(1));
        Assert.Equal(1, result.NumeratorCoefficientAt(1));
        Assert.Equal(1, result.DenominatorTermCount);
        Assert.Equal(0, result.DenominatorDegreeAt(0));
        Assert.Equal(1, result.DenominatorCoefficientAt(0));
    }
}
