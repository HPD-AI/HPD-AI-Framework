using System;
using HPD.Math.Core;
using HPD.Math.Numerics;

namespace HPD.Math.Tests;

[FieldExtensionScope(typeof(ModInt<P7>), typeof(ModIntOps<P7>), Terms = 3, Handles = 8, Workspace = 4)]
public partial struct FiniteFieldScopeExample
{
    partial void Build(ref Scope q)
    {
        ReadOnlySpan<int> degrees = [0, 2];
        Span<ModInt<P7>> coefficients = stackalloc ModInt<P7>[2];
        coefficients[0] = new ModInt<P7>(1);
        coefficients[1] = new ModInt<P7>(1);

        q.SetDefiningPolynomial(degrees, coefficients);

        var alpha = q.Generator();
        q.Return(alpha * alpha);
    }
}

[FieldExtensionScope(typeof(Rational32), typeof(Rational32StatusFieldOps), Terms = 3, Handles = 8, Workspace = 4)]
public partial struct NumberFieldScopeExample
{
    partial void Build(ref Scope q)
    {
        ReadOnlySpan<int> degrees = [0, 2];
        Span<Rational32> coefficients = stackalloc Rational32[2];
        coefficients[0] = new Rational32(-2, 1);
        coefficients[1] = new Rational32(1, 1);

        q.SetDefiningPolynomial(degrees, coefficients);

        var alpha = q.Generator();
        q.Return(alpha * alpha);
    }
}

public sealed class FieldExtensionScopeTests
{
    [Fact]
    public void FieldExtensionScope_ReducesFiniteFieldGenerator()
    {
        var result = default(FiniteFieldScopeExample.Result);

        var status = new FiniteFieldScopeExample().Run(ref result);

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(1, result.TermCount);
        Assert.Equal(0, result.DegreeAt(0));
        Assert.Equal(new ModInt<P7>(6), result.CoefficientAt(0));
    }

    [Fact]
    public void FieldExtensionScope_ReducesNumberFieldGeneratorWithStatusWitness()
    {
        var result = default(NumberFieldScopeExample.Result);

        var status = new NumberFieldScopeExample().Run(ref result);

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(1, result.TermCount);
        Assert.Equal(0, result.DegreeAt(0));
        Assert.Equal(new Rational32(2, 1), result.CoefficientAt(0));
    }
}
