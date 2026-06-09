using System;
using HPD.Math.Core;
using HPD.Math.Numerics;

namespace HPD.Math.Tests;

[FieldExtensionContext(typeof(ModInt<P7>), typeof(ModIntOps<P7>), Terms = 3, Handles = 8, Workspace = 4)]
public readonly partial struct FiniteFieldContextExample;

[FieldExtensionContext(typeof(Rational32), typeof(Rational32StatusFieldOps), Terms = 3, Handles = 8, Workspace = 4)]
public readonly partial struct NumberFieldContextExample;

public sealed class FieldExtensionContextTests
{
    [Fact]
    public void FieldExtensionContext_ReducesFiniteFieldGeneratorAsFirstClassElement()
    {
        var status = FiniteFieldContextExample.TryCreateOps(
            [0, 2],
            [new ModInt<P7>(1), new ModInt<P7>(1)],
            out var field);
        Assert.Equal(AlgebraStatus.Ok, status);

        status = field.TryGenerator(out var alpha);
        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(1, alpha.TermCount);
        Assert.Equal(1, alpha.DegreeAt(0));
        Assert.Equal(new ModInt<P7>(1), alpha.CoefficientAt(0));

        status = field.TryMul(alpha, alpha, out var result);
        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(1, result.TermCount);
        Assert.Equal(0, result.DegreeAt(0));
        Assert.Equal(new ModInt<P7>(6), result.CoefficientAt(0));
    }

    [Fact]
    public void FieldExtensionContext_ReducesNumberFieldGeneratorWithStatusWitnessAsFirstClassElement()
    {
        var status = NumberFieldContextExample.TryCreateOps(
            [0, 2],
            [new Rational32(-2, 1), new Rational32(1, 1)],
            out var field);
        Assert.Equal(AlgebraStatus.Ok, status);

        status = field.TryGenerator(out var alpha);
        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(1, alpha.TermCount);
        Assert.Equal(1, alpha.DegreeAt(0));
        Assert.Equal(new Rational32(1, 1), alpha.CoefficientAt(0));

        status = field.TryMul(alpha, alpha, out var result);
        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(1, result.TermCount);
        Assert.Equal(0, result.DegreeAt(0));
        Assert.Equal(new Rational32(2, 1), result.CoefficientAt(0));
    }
}
