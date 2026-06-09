using System;
using HPD.Math.Algebra;
using HPD.Math.Core;
using HPD.Math.Finite;
using HPD.Math.Numerics;

namespace HPD.Math.Tests;

public sealed class PolynomialIrreducibilityKernelTests
{
    [Fact]
    public void TryIsIrreducibleOverFiniteField_DetectsSmallDegreeCases()
    {
        ReadOnlySpan<int> irreducibleDegrees = [0, 2];
        ReadOnlySpan<ModInt<P7>> irreducibleCoefficients = [new(1), new(1)];
        var irreducible = new SparsePolynomialView<ModInt<P7>>(
            new FinsuppView<int, ModInt<P7>>(irreducibleDegrees, irreducibleCoefficients));

        var status = PolynomialIrreducibilityKernels.TryIsIrreducibleOverFiniteField(
            irreducible,
            out var isIrreducible,
            new ModIntOps<P7>(),
            new ModIntOps<P7>());

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.True(isIrreducible);

        ReadOnlySpan<int> reducibleDegrees = [0, 2];
        ReadOnlySpan<ModInt<P7>> reducibleCoefficients = [new(-1), new(1)];
        var reducible = new SparsePolynomialView<ModInt<P7>>(
            new FinsuppView<int, ModInt<P7>>(reducibleDegrees, reducibleCoefficients));

        status = PolynomialIrreducibilityKernels.TryIsIrreducibleOverFiniteField(
            reducible,
            out isIrreducible,
            new ModIntOps<P7>(),
            new ModIntOps<P7>());

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.False(isIrreducible);
    }
}
