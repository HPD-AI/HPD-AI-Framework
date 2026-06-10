using System;
using HPD.Math.Algebra;
using HPD.Math.Core;
using HPD.Math.Finite;

namespace HPD.Math.Tests;

public sealed class SparsePolynomialKernelTests
{
    [Fact]
    public void BasicKernels_ConstructSubtractNegateAndScale()
    {
        Span<int> monomialDegrees = stackalloc int[1];
        Span<int> monomialCoefficients = stackalloc int[1];
        var monomial = new SparsePolynomialBuilder<int>(monomialDegrees, monomialCoefficients);

        Assert.Equal(
            AlgebraStatus.Ok,
            SparsePolynomialKernels.TryMonomial(3, 5, ref monomial, new CheckedInt32RingOps()));
        Assert.Equal(3, monomial.AsView().Degree);

        ReadOnlySpan<int> leftDegrees = [0, 2];
        ReadOnlySpan<int> leftCoefficients = [4, 7];
        ReadOnlySpan<int> rightDegrees = [0, 1];
        ReadOnlySpan<int> rightCoefficients = [1, 3];
        var left = new SparsePolynomialView<int>(new FinsuppView<int, int>(leftDegrees, leftCoefficients));
        var right = new SparsePolynomialView<int>(new FinsuppView<int, int>(rightDegrees, rightCoefficients));

        Span<int> destinationDegrees = stackalloc int[3];
        Span<int> destinationCoefficients = stackalloc int[3];
        var destination = new SparsePolynomialBuilder<int>(destinationDegrees, destinationCoefficients);

        Assert.Equal(
            AlgebraStatus.Ok,
            SparsePolynomialKernels.TrySub(left, right, ref destination, new CheckedInt32RingOps()));
        Assert.Equal(3, destination.AsView().CoefficientAt(0));
        Assert.Equal(-3, destination.AsView().CoefficientAt(1));
        Assert.Equal(7, destination.AsView().CoefficientAt(2));

        Assert.Equal(
            AlgebraStatus.Ok,
            SparsePolynomialKernels.TryNeg(right, ref destination, new CheckedInt32RingOps()));
        Assert.Equal(-1, destination.AsView().CoefficientAt(0));

        Assert.Equal(
            AlgebraStatus.Ok,
            SparsePolynomialKernels.TryScale(right, 2, ref destination, new CheckedInt32RingOps()));
        Assert.Equal(6, destination.AsView().CoefficientAt(1));
    }

    [Fact]
    public void TryMul_MultipliesIntoCallerOwnedStorage()
    {
        ReadOnlySpan<int> leftDegrees = [0, 1];
        ReadOnlySpan<int> leftCoefficients = [1, 2];
        ReadOnlySpan<int> rightDegrees = [0, 2];
        ReadOnlySpan<int> rightCoefficients = [3, 4];

        var left = new SparsePolynomialView<int>(new FinsuppView<int, int>(leftDegrees, leftCoefficients));
        var right = new SparsePolynomialView<int>(new FinsuppView<int, int>(rightDegrees, rightCoefficients));

        Span<int> destinationDegrees = stackalloc int[4];
        Span<int> destinationCoefficients = stackalloc int[4];
        Span<int> workspaceDegrees = stackalloc int[4];
        Span<int> workspaceCoefficients = stackalloc int[4];
        var destination = new SparsePolynomialBuilder<int>(destinationDegrees, destinationCoefficients);

        var status = left.TryMul(
            right,
            ref destination,
            workspaceDegrees,
            workspaceCoefficients,
            new CheckedInt32RingOps());

        Assert.Equal(AlgebraStatus.Ok, status);

        var result = destination.AsView();
        Assert.Equal(4, result.TermCount);
        Assert.Equal(0, result.DegreeAt(0));
        Assert.Equal(3, result.CoefficientAt(0));
        Assert.Equal(1, result.DegreeAt(1));
        Assert.Equal(6, result.CoefficientAt(1));
        Assert.Equal(2, result.DegreeAt(2));
        Assert.Equal(4, result.CoefficientAt(2));
        Assert.Equal(3, result.DegreeAt(3));
        Assert.Equal(8, result.CoefficientAt(3));
    }

    [Fact]
    public void Builder_RejectsOutOfOrderTerms()
    {
        Span<int> degrees = stackalloc int[2];
        Span<int> coefficients = stackalloc int[2];
        var builder = new SparsePolynomialBuilder<int>(degrees, coefficients);

        Assert.Equal(AlgebraStatus.Ok, builder.TryAppendTerm(2, 5, new CheckedInt32RingOps()));
        Assert.Equal(AlgebraStatus.InvalidInput, builder.TryAppendTerm(1, 3, new CheckedInt32RingOps()));
    }

    [Fact]
    public void DensePolynomialKernels_ConvertSparseToDenseAndBack()
    {
        ReadOnlySpan<int> degrees = [0, 2];
        ReadOnlySpan<int> coefficients = [4, 7];
        var sparse = new SparsePolynomialView<int>(new FinsuppView<int, int>(degrees, coefficients));

        Span<int> denseStorage = stackalloc int[3];
        Span<int> denseWorkspace = stackalloc int[3];
        var dense = new DensePolynomialBuilder<int>(denseStorage);

        Assert.Equal(
            AlgebraStatus.Ok,
            DensePolynomialKernels.TryFromSparse(sparse, ref dense, denseWorkspace, new CheckedInt32RingOps()));

        var denseView = dense.AsView();
        Assert.Equal(3, denseView.CoefficientCount);
        Assert.Equal(0, denseView.Coefficients[1]);

        Span<int> sparseDegrees = stackalloc int[2];
        Span<int> sparseCoefficients = stackalloc int[2];
        var sparseBuilder = new SparsePolynomialBuilder<int>(sparseDegrees, sparseCoefficients);

        Assert.Equal(
            AlgebraStatus.Ok,
            DensePolynomialKernels.TryToSparse(denseView, ref sparseBuilder, new CheckedInt32RingOps()));
        Assert.Equal(2, sparseBuilder.AsView().TermCount);
        Assert.Equal(2, sparseBuilder.AsView().Degree);
    }

    [Fact]
    public void TryDivMod_DividesOverFieldIntoCallerOwnedStorage()
    {
        // (x^2 + 1) / (x + 1) over F7 = x - 1 with remainder 2.
        ReadOnlySpan<int> dividendDegrees = [0, 2];
        ReadOnlySpan<int> dividendCoefficients = [1, 1];
        ReadOnlySpan<int> divisorDegrees = [0, 1];
        ReadOnlySpan<int> divisorCoefficients = [1, 1];

        var dividend = new SparsePolynomialView<int>(new FinsuppView<int, int>(dividendDegrees, dividendCoefficients));
        var divisor = new SparsePolynomialView<int>(new FinsuppView<int, int>(divisorDegrees, divisorCoefficients));

        Span<int> quotientDegrees = stackalloc int[2];
        Span<int> quotientCoefficients = stackalloc int[2];
        Span<int> remainderDegrees = stackalloc int[1];
        Span<int> remainderCoefficients = stackalloc int[1];
        Span<int> quotientWorkspace = stackalloc int[2];
        Span<int> remainderWorkspace = stackalloc int[3];
        var quotient = new SparsePolynomialBuilder<int>(quotientDegrees, quotientCoefficients);
        var remainder = new SparsePolynomialBuilder<int>(remainderDegrees, remainderCoefficients);

        var status = SparsePolynomialKernels.TryDivMod(
            dividend,
            divisor,
            ref quotient,
            ref remainder,
            quotientWorkspace,
            remainderWorkspace,
            new Mod7FieldOps());

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(6, quotient.AsView().CoefficientAt(0));
        Assert.Equal(1, quotient.AsView().CoefficientAt(1));
        Assert.Equal(2, remainder.AsView().CoefficientAt(0));
    }

    [Fact]
    public void TryGcdAndExtendedGcd_ReturnMonicBezoutIdentity()
    {
        // gcd(x^2 - 1, x - 1) over F7 is x - 1.
        ReadOnlySpan<int> leftDegrees = [0, 2];
        ReadOnlySpan<int> leftCoefficients = [6, 1];
        ReadOnlySpan<int> rightDegrees = [0, 1];
        ReadOnlySpan<int> rightCoefficients = [6, 1];
        var left = new SparsePolynomialView<int>(new FinsuppView<int, int>(leftDegrees, leftCoefficients));
        var right = new SparsePolynomialView<int>(new FinsuppView<int, int>(rightDegrees, rightCoefficients));

        Span<int> gcdDegrees = stackalloc int[2];
        Span<int> gcdCoefficients = stackalloc int[2];
        Span<int> leftWorkspace = stackalloc int[3];
        Span<int> rightWorkspace = stackalloc int[3];
        Span<int> remainderWorkspace = stackalloc int[3];
        var gcd = new SparsePolynomialBuilder<int>(gcdDegrees, gcdCoefficients);

        var status = SparsePolynomialKernels.TryGcd(
            left,
            right,
            ref gcd,
            leftWorkspace,
            rightWorkspace,
            remainderWorkspace,
            new Mod7FieldOps());

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(6, gcd.AsView().CoefficientAt(0));
        Assert.Equal(1, gcd.AsView().CoefficientAt(1));

        Span<int> bezoutLeftDegrees = stackalloc int[1];
        Span<int> bezoutLeftCoefficients = stackalloc int[1];
        Span<int> bezoutRightDegrees = stackalloc int[1];
        Span<int> bezoutRightCoefficients = stackalloc int[1];
        var bezoutLeft = new SparsePolynomialBuilder<int>(bezoutLeftDegrees, bezoutLeftCoefficients);
        var bezoutRight = new SparsePolynomialBuilder<int>(bezoutRightDegrees, bezoutRightCoefficients);

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
        var workspace = new SparsePolynomialEuclideanWorkspace<int>(
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

        status = SparsePolynomialKernels.TryExtendedGcd(
            left,
            right,
            ref gcd,
            ref bezoutLeft,
            ref bezoutRight,
            workspace,
            new Mod7FieldOps());

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.True(bezoutLeft.AsView().IsZero);
        Assert.Equal(1, bezoutRight.AsView().CoefficientAt(0));
        Assert.Equal(6, gcd.AsView().CoefficientAt(0));
        Assert.Equal(1, gcd.AsView().CoefficientAt(1));
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
