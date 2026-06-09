using System;
using HPD.Math.Algebra;
using HPD.Math.Core;

namespace HPD.Math.Tests;

public sealed class SparseMvPolynomialKernelTests
{
    [Fact]
    public void MonomialKernels_MultiplyDivideAndCompare()
    {
        ReadOnlySpan<int> left = [2, 0, 1];
        ReadOnlySpan<int> right = [0, 3, 4];
        Span<int> product = stackalloc int[3];
        Span<int> quotient = stackalloc int[3];

        Assert.Equal(AlgebraStatus.Ok, MonomialKernels.TryMul(left, right, product));
        Assert.True(product.SequenceEqual([2, 3, 5]));

        Assert.Equal(AlgebraStatus.Ok, MonomialKernels.TryDivides(left, product, out var divides));
        Assert.True(divides);

        Assert.Equal(AlgebraStatus.Ok, MonomialKernels.TryDiv(product, left, quotient));
        Assert.True(quotient.SequenceEqual(right));

        Assert.Equal(Ordering.Greater, new GradedLexMonomialOrderOps().Compare(product, right));
        Assert.Equal(Ordering.Less, new GradedReverseLexMonomialOrderOps().Compare([1, 0, 1], [0, 2, 0]));
    }

    [Fact]
    public void SparseMvPolynomialKernels_AddAndSubtract()
    {
        // left = 4 + 2x, right = -4 + 3y.
        ReadOnlySpan<int> leftExponents = [0, 0, 1, 0];
        ReadOnlySpan<int> rightExponents = [0, 0, 0, 1];
        ReadOnlySpan<int> leftCoefficients = [4, 2];
        ReadOnlySpan<int> rightCoefficients = [-4, 3];
        var left = new SparseMvPolynomialView<int>(2, leftExponents, leftCoefficients);
        var right = new SparseMvPolynomialView<int>(2, rightExponents, rightCoefficients);

        Span<int> destinationExponents = stackalloc int[6];
        Span<int> destinationCoefficients = stackalloc int[3];
        var destination = new SparseMvPolynomialBuilder<int>(2, destinationExponents, destinationCoefficients);

        var status = SparseMvPolynomialKernels.TryAdd(
            left,
            right,
            ref destination,
            new CheckedInt32RingOps(),
            new GradedLexMonomialOrderOps());

        Assert.Equal(AlgebraStatus.Ok, status);
        var result = destination.AsView();
        Assert.Equal(2, result.TermCount);
        Assert.True(result.MonomialAt(0).SequenceEqual([0, 1]));
        Assert.Equal(3, result.CoefficientAt(0));
        Assert.True(result.MonomialAt(1).SequenceEqual([1, 0]));
        Assert.Equal(2, result.CoefficientAt(1));

        status = SparseMvPolynomialKernels.TrySub(
            left,
            right,
            ref destination,
            new CheckedInt32RingOps(),
            new GradedLexMonomialOrderOps());

        Assert.Equal(AlgebraStatus.Ok, status);
        result = destination.AsView();
        Assert.Equal(3, result.TermCount);
        Assert.Equal(8, result.CoefficientAt(0));
        Assert.Equal(-3, result.CoefficientAt(1));
        Assert.Equal(2, result.CoefficientAt(2));
    }

    [Fact]
    public void SparseMvPolynomialKernels_MultiplyAndFindLeadingTerm()
    {
        // (x + y)(x - y) = x^2 - y^2.
        ReadOnlySpan<int> leftExponents = [0, 1, 1, 0];
        ReadOnlySpan<int> rightExponents = [0, 1, 1, 0];
        ReadOnlySpan<int> leftCoefficients = [1, 1];
        ReadOnlySpan<int> rightCoefficients = [-1, 1];
        var left = new SparseMvPolynomialView<int>(2, leftExponents, leftCoefficients);
        var right = new SparseMvPolynomialView<int>(2, rightExponents, rightCoefficients);

        Span<int> destinationExponents = stackalloc int[4];
        Span<int> destinationCoefficients = stackalloc int[2];
        Span<int> workspaceExponents = stackalloc int[8];
        Span<int> workspaceCoefficients = stackalloc int[4];
        var destination = new SparseMvPolynomialBuilder<int>(2, destinationExponents, destinationCoefficients);

        var status = SparseMvPolynomialKernels.TryMul(
            left,
            right,
            ref destination,
            workspaceExponents,
            workspaceCoefficients,
            new CheckedInt32RingOps(),
            new GradedLexMonomialOrderOps());

        Assert.Equal(AlgebraStatus.Ok, status);
        var result = destination.AsView();
        Assert.Equal(2, result.TermCount);
        Assert.True(result.MonomialAt(0).SequenceEqual([0, 2]));
        Assert.Equal(-1, result.CoefficientAt(0));
        Assert.True(result.MonomialAt(1).SequenceEqual([2, 0]));
        Assert.Equal(1, result.CoefficientAt(1));

        status = SparseMvPolynomialKernels.TryLeadingTerm(
            result,
            new CheckedInt32RingOps(),
            new GradedLexMonomialOrderOps(),
            out var leadingIndex);

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(1, leadingIndex);
        Assert.True(result.MonomialAt(leadingIndex).SequenceEqual([2, 0]));
    }

    [Fact]
    public void SparseMvPolynomialKernels_ConstructConstantsVariablesAndEvaluate()
    {
        ReadOnlySpan<int> exponents = [0, 0, 0, 1, 1, 0];
        ReadOnlySpan<int> coefficients = [4, 3, 2];
        ReadOnlySpan<int> point = [5, 7];
        var polynomial = new SparseMvPolynomialView<int>(2, exponents, coefficients);

        var value = 0;
        var status = SparseMvPolynomialKernels.TryEvaluate(
            polynomial,
            point,
            ref value,
            new CheckedInt32RingOps(),
            new GradedLexMonomialOrderOps());

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(35, value);

        Span<int> constantExponents = stackalloc int[2];
        Span<int> constantCoefficients = stackalloc int[1];
        var constant = new SparseMvPolynomialBuilder<int>(2, constantExponents, constantCoefficients);

        status = SparseMvPolynomialKernels.TryConstant(
            9,
            ref constant,
            new CheckedInt32RingOps(),
            new GradedLexMonomialOrderOps());

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.True(constant.AsView().MonomialAt(0).SequenceEqual([0, 0]));
        Assert.Equal(9, constant.AsView().CoefficientAt(0));

        Span<int> variableExponents = stackalloc int[2];
        Span<int> variableCoefficients = stackalloc int[1];
        var variable = new SparseMvPolynomialBuilder<int>(2, variableExponents, variableCoefficients);

        status = SparseMvPolynomialKernels.TryVariable(
            1,
            ref variable,
            new CheckedInt32RingOps(),
            new GradedLexMonomialOrderOps());

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.True(variable.AsView().MonomialAt(0).SequenceEqual([0, 1]));
        Assert.Equal(1, variable.AsView().CoefficientAt(0));
    }
}
