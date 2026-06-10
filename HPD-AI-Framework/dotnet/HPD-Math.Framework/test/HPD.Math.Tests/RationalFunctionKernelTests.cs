using System;
using HPD.Math.Algebra;
using HPD.Math.Core;
using HPD.Math.Finite;

namespace HPD.Math.Tests;

public sealed class RationalFunctionKernelTests
{
    [Fact]
    public void TryAdd_CrossMultipliesIntoCallerOwnedStorage()
    {
        // 1/x + 1/(x + 1) = (2x + 1)/(x^2 + x).
        ReadOnlySpan<int> oneDegree = [0];
        ReadOnlySpan<int> oneCoefficient = [1];
        ReadOnlySpan<int> xDegree = [1];
        ReadOnlySpan<int> xCoefficient = [1];
        ReadOnlySpan<int> xPlusOneDegrees = [0, 1];
        ReadOnlySpan<int> xPlusOneCoefficients = [1, 1];

        var left = new RationalFunctionView<int>(
            new SparsePolynomialView<int>(new FinsuppView<int, int>(oneDegree, oneCoefficient)),
            new SparsePolynomialView<int>(new FinsuppView<int, int>(xDegree, xCoefficient)));
        var right = new RationalFunctionView<int>(
            new SparsePolynomialView<int>(new FinsuppView<int, int>(oneDegree, oneCoefficient)),
            new SparsePolynomialView<int>(new FinsuppView<int, int>(xPlusOneDegrees, xPlusOneCoefficients)));

        Span<int> numeratorDegrees = stackalloc int[2];
        Span<int> numeratorCoefficients = stackalloc int[2];
        Span<int> denominatorDegrees = stackalloc int[2];
        Span<int> denominatorCoefficients = stackalloc int[2];
        var destination = new RationalFunctionBuilder<int>(
            numeratorDegrees,
            numeratorCoefficients,
            denominatorDegrees,
            denominatorCoefficients);

        Span<int> leftProductDegrees = stackalloc int[2];
        Span<int> leftProductCoefficients = stackalloc int[2];
        Span<int> rightProductDegrees = stackalloc int[2];
        Span<int> rightProductCoefficients = stackalloc int[2];
        Span<int> multiplyWorkspaceDegrees = stackalloc int[4];
        Span<int> multiplyWorkspaceCoefficients = stackalloc int[4];
        var workspace = new RationalFunctionArithmeticWorkspace<int>(
            leftProductDegrees,
            leftProductCoefficients,
            rightProductDegrees,
            rightProductCoefficients,
            multiplyWorkspaceDegrees,
            multiplyWorkspaceCoefficients);

        var status = RationalFunctionKernels.TryAdd(
            left,
            right,
            ref destination,
            workspace,
            new CheckedInt32RingOps());

        Assert.Equal(AlgebraStatus.Ok, status);
        var result = destination.AsView();
        Assert.Equal(2, result.Numerator.TermCount);
        Assert.Equal(1, result.Numerator.CoefficientAt(0));
        Assert.Equal(2, result.Numerator.CoefficientAt(1));
        Assert.Equal(2, result.Denominator.TermCount);
        Assert.Equal(1, result.Denominator.CoefficientAt(0));
        Assert.Equal(1, result.Denominator.CoefficientAt(1));
    }

    [Fact]
    public void TryNormalize_ReducesByPolynomialGcd()
    {
        // (x^2 - 1)/(x - 1) over F7 = x + 1.
        ReadOnlySpan<int> numeratorDegrees = [0, 2];
        ReadOnlySpan<int> numeratorCoefficients = [6, 1];
        ReadOnlySpan<int> denominatorDegrees = [0, 1];
        ReadOnlySpan<int> denominatorCoefficients = [6, 1];

        var value = new RationalFunctionView<int>(
            new SparsePolynomialView<int>(new FinsuppView<int, int>(numeratorDegrees, numeratorCoefficients)),
            new SparsePolynomialView<int>(new FinsuppView<int, int>(denominatorDegrees, denominatorCoefficients)));

        Span<int> normalizedNumeratorDegrees = stackalloc int[2];
        Span<int> normalizedNumeratorCoefficients = stackalloc int[2];
        Span<int> normalizedDenominatorDegrees = stackalloc int[1];
        Span<int> normalizedDenominatorCoefficients = stackalloc int[1];
        var destination = new RationalFunctionBuilder<int>(
            normalizedNumeratorDegrees,
            normalizedNumeratorCoefficients,
            normalizedDenominatorDegrees,
            normalizedDenominatorCoefficients);

        Span<int> gcdDegrees = stackalloc int[2];
        Span<int> gcdCoefficients = stackalloc int[2];
        Span<int> numeratorRemainderDegrees = stackalloc int[1];
        Span<int> numeratorRemainderCoefficients = stackalloc int[1];
        Span<int> denominatorRemainderDegrees = stackalloc int[1];
        Span<int> denominatorRemainderCoefficients = stackalloc int[1];
        Span<int> gcdLeftWorkspace = stackalloc int[3];
        Span<int> gcdRightWorkspace = stackalloc int[3];
        Span<int> gcdRemainderWorkspace = stackalloc int[3];
        Span<int> quotientWorkspace = stackalloc int[3];
        Span<int> remainderWorkspace = stackalloc int[3];
        var workspace = new RationalFunctionNormalizationWorkspace<int>(
            gcdDegrees,
            gcdCoefficients,
            numeratorRemainderDegrees,
            numeratorRemainderCoefficients,
            denominatorRemainderDegrees,
            denominatorRemainderCoefficients,
            gcdLeftWorkspace,
            gcdRightWorkspace,
            gcdRemainderWorkspace,
            quotientWorkspace,
            remainderWorkspace);

        var status = RationalFunctionKernels.TryNormalize(
            value,
            ref destination,
            workspace,
            new Mod7FieldOps());

        Assert.Equal(AlgebraStatus.Ok, status);
        var result = destination.AsView();
        Assert.Equal(2, result.Numerator.TermCount);
        Assert.Equal(1, result.Numerator.CoefficientAt(0));
        Assert.Equal(1, result.Numerator.CoefficientAt(1));
        Assert.Equal(1, result.Denominator.TermCount);
        Assert.Equal(1, result.Denominator.CoefficientAt(0));
    }

    private readonly struct Mod7FieldOps : IFieldOps<int>
    {
        public int Zero => 0;
        public int One => 1;

        public bool Eq(in int left, in int right) => Mod(left) == Mod(right);

        public void Add(ref int destination, in int left, in int right) =>
            destination = Mod(left + right);

        public void Sub(ref int destination, in int left, in int right) =>
            destination = Mod(left - right);

        public void Mul(ref int destination, in int left, in int right) =>
            destination = Mod(left * right);

        public void Neg(ref int destination, in int value) =>
            destination = Mod(-value);

        public AlgebraStatus TryInvert(ref int destination, in int value)
        {
            var normalized = Mod(value);
            if (normalized == 0)
                return AlgebraStatus.DivisionByZero;

            for (var i = 1; i < 7; i++)
            {
                if (Mod(normalized * i) != 1)
                    continue;

                destination = i;
                return AlgebraStatus.Ok;
            }

            return AlgebraStatus.NonInvertible;
        }

        private static int Mod(int value)
        {
            var result = value % 7;
            return result < 0 ? result + 7 : result;
        }
    }
}
