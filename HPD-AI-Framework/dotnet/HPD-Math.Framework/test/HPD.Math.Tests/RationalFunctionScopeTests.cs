using System;
using HPD.Math.Core;

namespace HPD.Math.Tests;

[RationalFunctionScope(typeof(int), typeof(GeneratedMod7FieldOps), Terms = 3, Handles = 4, Workspace = 4)]
public partial struct Mod7RationalFunctionScopeExample
{
    partial void Build(ref Scope r)
    {
        var value = r.FromPolynomials(
            [0, 2],
            [6, 1],
            [0, 1],
            [6, 1]);

        r.Return(r.Normalize(value));
    }
}

[RationalFunctionScope(typeof(int), typeof(GeneratedMod7FieldOps), Terms = 3, Handles = 1, Workspace = 4)]
public partial struct TooSmallRationalFunctionScopeExample
{
    partial void Build(ref Scope r)
    {
        var value = r.FromPolynomials(
            [0, 2],
            [6, 1],
            [0, 1],
            [6, 1]);

        r.Return(r.Normalize(value));
    }
}

public sealed class RationalFunctionScopeTests
{
    [Fact]
    public void RationalFunctionScope_GeneratesScopeLocalNormalizationSyntax()
    {
        var result = default(Mod7RationalFunctionScopeExample.Result);

        var status = new Mod7RationalFunctionScopeExample().Run(ref result);

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

    [Fact]
    public void RationalFunctionScope_RecordsHandleCapacityFailureInScopeStatus()
    {
        var result = default(TooSmallRationalFunctionScopeExample.Result);

        var status = new TooSmallRationalFunctionScopeExample().Run(ref result);

        Assert.Equal(AlgebraStatus.InsufficientDestination, status);
        Assert.Equal(0, result.NumeratorTermCount);
        Assert.Equal(0, result.DenominatorTermCount);
    }
}
