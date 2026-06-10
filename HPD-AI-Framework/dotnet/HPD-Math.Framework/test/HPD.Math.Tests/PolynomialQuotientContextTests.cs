using System;
using HPD.Math.Core;

namespace HPD.Math.Tests;

[PolynomialQuotientContext(typeof(int), typeof(GeneratedMod7FieldOps), Terms = 3, Handles = 8, Workspace = 4)]
public readonly partial struct Mod7PolynomialQuotientContextExample;

public sealed class PolynomialQuotientContextTests
{
    [Fact]
    public void PolynomialQuotientContext_GeneratesFirstClassElements()
    {
        var status = Mod7PolynomialQuotientContextExample.TryCreateOps([0, 2], [1, 1], out var q);
        Assert.Equal(AlgebraStatus.Ok, status);

        status = q.TryGenerator(out var x);
        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(1, x.TermCount);
        Assert.Equal(1, x.DegreeAt(0));
        Assert.Equal(1, x.CoefficientAt(0));

        status = q.TryMul(x, x, out var result);
        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(1, result.TermCount);
        Assert.Equal(0, result.DegreeAt(0));
        Assert.Equal(6, result.CoefficientAt(0));
    }
}
