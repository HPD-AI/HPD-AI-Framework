using System;
using HPD.Math.Algebra;
using HPD.Math.Core;
using HPD.Math.Finite;

namespace HPD.Math.Tests;

public sealed class PolynomialQuotientKernelTests
{
    [Fact]
    public void TryReduce_ReducesModuloPolynomial()
    {
        ReadOnlySpan<int> valueDegrees = [2];
        ReadOnlySpan<int> valueCoefficients = [1];
        ReadOnlySpan<int> modulusDegrees = [0, 2];
        ReadOnlySpan<int> modulusCoefficients = [1, 1];

        var value = new SparsePolynomialView<int>(new FinsuppView<int, int>(valueDegrees, valueCoefficients));
        var modulus = new SparsePolynomialView<int>(new FinsuppView<int, int>(modulusDegrees, modulusCoefficients));

        Span<int> resultDegrees = stackalloc int[1];
        Span<int> resultCoefficients = stackalloc int[1];
        var result = new PolynomialQuotientBuilder<int>(resultDegrees, resultCoefficients);

        Span<int> quotientDegrees = stackalloc int[1];
        Span<int> quotientCoefficients = stackalloc int[1];
        Span<int> remainderDegrees = stackalloc int[1];
        Span<int> remainderCoefficients = stackalloc int[1];
        Span<int> quotientWorkspace = stackalloc int[1];
        Span<int> remainderWorkspace = stackalloc int[3];
        var workspace = new PolynomialQuotientReductionWorkspace<int>(
            quotientDegrees,
            quotientCoefficients,
            remainderDegrees,
            remainderCoefficients,
            quotientWorkspace,
            remainderWorkspace);

        var status = PolynomialQuotientKernels.TryReduce(
            value,
            modulus,
            ref result,
            workspace,
            new GeneratedMod7FieldOps());

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(1, result.AsView().Representative.TermCount);
        Assert.Equal(6, result.AsView().Representative.CoefficientAt(0));
    }

    [Fact]
    public void TryMul_ReducesProductModuloPolynomial()
    {
        ReadOnlySpan<int> xDegrees = [1];
        ReadOnlySpan<int> xCoefficients = [1];
        ReadOnlySpan<int> modulusDegrees = [0, 2];
        ReadOnlySpan<int> modulusCoefficients = [1, 1];

        var x = new PolynomialQuotientView<int>(
            new SparsePolynomialView<int>(new FinsuppView<int, int>(xDegrees, xCoefficients)));
        var modulus = new SparsePolynomialView<int>(new FinsuppView<int, int>(modulusDegrees, modulusCoefficients));

        Span<int> resultDegrees = stackalloc int[1];
        Span<int> resultCoefficients = stackalloc int[1];
        var result = new PolynomialQuotientBuilder<int>(resultDegrees, resultCoefficients);

        Span<int> intermediateDegrees = stackalloc int[1];
        Span<int> intermediateCoefficients = stackalloc int[1];
        Span<int> multiplyWorkspaceDegrees = stackalloc int[1];
        Span<int> multiplyWorkspaceCoefficients = stackalloc int[1];
        Span<int> quotientDegrees = stackalloc int[1];
        Span<int> quotientCoefficients = stackalloc int[1];
        Span<int> remainderDegrees = stackalloc int[1];
        Span<int> remainderCoefficients = stackalloc int[1];
        Span<int> quotientWorkspace = stackalloc int[1];
        Span<int> remainderWorkspace = stackalloc int[3];
        var reduction = new PolynomialQuotientReductionWorkspace<int>(
            quotientDegrees,
            quotientCoefficients,
            remainderDegrees,
            remainderCoefficients,
            quotientWorkspace,
            remainderWorkspace);
        var workspace = new PolynomialQuotientArithmeticWorkspace<int>(
            intermediateDegrees,
            intermediateCoefficients,
            multiplyWorkspaceDegrees,
            multiplyWorkspaceCoefficients,
            reduction);

        var status = PolynomialQuotientKernels.TryMul(
            x,
            x,
            modulus,
            ref result,
            workspace,
            new GeneratedMod7FieldOps());

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(1, result.AsView().Representative.TermCount);
        Assert.Equal(6, result.AsView().Representative.CoefficientAt(0));
    }

    [Fact]
    public void TryInvert_UsesExtendedGcdModuloPolynomial()
    {
        ReadOnlySpan<int> xDegrees = [1];
        ReadOnlySpan<int> xCoefficients = [1];
        ReadOnlySpan<int> modulusDegrees = [0, 2];
        ReadOnlySpan<int> modulusCoefficients = [1, 1];

        var x = new PolynomialQuotientView<int>(
            new SparsePolynomialView<int>(new FinsuppView<int, int>(xDegrees, xCoefficients)));
        var modulus = new SparsePolynomialView<int>(new FinsuppView<int, int>(modulusDegrees, modulusCoefficients));

        Span<int> inverseDegrees = stackalloc int[1];
        Span<int> inverseCoefficients = stackalloc int[1];
        var inverse = new PolynomialQuotientBuilder<int>(inverseDegrees, inverseCoefficients);

        Span<int> gcdDegrees = stackalloc int[1];
        Span<int> gcdCoefficients = stackalloc int[1];
        Span<int> bezoutValueDegrees = stackalloc int[2];
        Span<int> bezoutValueCoefficients = stackalloc int[2];
        Span<int> bezoutModulusDegrees = stackalloc int[1];
        Span<int> bezoutModulusCoefficients = stackalloc int[1];
        Span<int> oldR = stackalloc int[3];
        Span<int> r = stackalloc int[3];
        Span<int> rem = stackalloc int[3];
        Span<int> q = stackalloc int[3];
        Span<int> oldU = stackalloc int[3];
        Span<int> u = stackalloc int[3];
        Span<int> nextU = stackalloc int[3];
        Span<int> oldV = stackalloc int[3];
        Span<int> v = stackalloc int[3];
        Span<int> nextV = stackalloc int[3];
        Span<int> product = stackalloc int[3];
        var euclidean = new SparsePolynomialEuclideanWorkspace<int>(
            oldR,
            r,
            rem,
            q,
            oldU,
            u,
            nextU,
            oldV,
            v,
            nextV,
            product);

        Span<int> quotientDegrees = stackalloc int[1];
        Span<int> quotientCoefficients = stackalloc int[1];
        Span<int> remainderDegrees = stackalloc int[2];
        Span<int> remainderCoefficients = stackalloc int[2];
        Span<int> quotientWorkspace = stackalloc int[2];
        Span<int> remainderWorkspace = stackalloc int[2];
        var reduction = new PolynomialQuotientReductionWorkspace<int>(
            quotientDegrees,
            quotientCoefficients,
            remainderDegrees,
            remainderCoefficients,
            quotientWorkspace,
            remainderWorkspace);
        var workspace = new PolynomialQuotientInversionWorkspace<int>(
            gcdDegrees,
            gcdCoefficients,
            bezoutValueDegrees,
            bezoutValueCoefficients,
            bezoutModulusDegrees,
            bezoutModulusCoefficients,
            euclidean,
            reduction);

        var status = PolynomialQuotientKernels.TryInvert(
            x,
            modulus,
            ref inverse,
            workspace,
            new GeneratedMod7FieldOps());

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(1, inverse.AsView().Representative.TermCount);
        Assert.Equal(1, inverse.AsView().Representative.DegreeAt(0));
        Assert.Equal(6, inverse.AsView().Representative.CoefficientAt(0));
    }
}
