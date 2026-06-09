using System;
using HPD.Math.Algebra;
using HPD.Math.Core;
using HPD.Math.Finite;
using HPD.Math.Numerics;

namespace HPD.Math.Tests;

[PrimeModulus(7)]
public readonly partial struct P7;

[PrimeModulus(2)]
public readonly partial struct P2;

[Precision(3)]
public readonly partial struct N3;

[Precision(2)]
public readonly partial struct N2;

public sealed class NumericContextTests
{
    [Fact]
    public void ModIntOps_ProvideFieldArithmeticAndEnumeration()
    {
        var ops = new ModIntOps<P7>();
        var left = new ModInt<P7>(5);
        var right = new ModInt<P7>(4);
        var result = ModInt<P7>.Zero;

        ops.Add(ref result, left, right);
        Assert.Equal(2, result.Value);

        ops.Mul(ref result, left, right);
        Assert.Equal(6, result.Value);

        Assert.Equal(AlgebraStatus.Ok, ops.TryInvert(ref result, right));
        Assert.Equal(2, result.Value);

        Span<ModInt<P7>> residues = stackalloc ModInt<P7>[7];
        Assert.Equal(AlgebraStatus.Ok, ops.TryFill(residues));
        Assert.Equal(0, residues[0].Value);
        Assert.Equal(6, residues[6].Value);
    }

    [Fact]
    public void ModInt_WorksAsSparsePolynomialFieldCoefficient()
    {
        ReadOnlySpan<int> dividendDegrees = [0, 2];
        ReadOnlySpan<ModInt<P7>> dividendCoefficients = [new(1), new(1)];
        ReadOnlySpan<int> divisorDegrees = [0, 1];
        ReadOnlySpan<ModInt<P7>> divisorCoefficients = [new(1), new(1)];

        var dividend = new SparsePolynomialView<ModInt<P7>>(
            new FinsuppView<int, ModInt<P7>>(dividendDegrees, dividendCoefficients));
        var divisor = new SparsePolynomialView<ModInt<P7>>(
            new FinsuppView<int, ModInt<P7>>(divisorDegrees, divisorCoefficients));

        Span<int> quotientDegrees = stackalloc int[2];
        Span<ModInt<P7>> quotientCoefficients = stackalloc ModInt<P7>[2];
        Span<int> remainderDegrees = stackalloc int[1];
        Span<ModInt<P7>> remainderCoefficients = stackalloc ModInt<P7>[1];
        Span<ModInt<P7>> quotientWorkspace = stackalloc ModInt<P7>[2];
        Span<ModInt<P7>> remainderWorkspace = stackalloc ModInt<P7>[3];
        var quotient = new SparsePolynomialBuilder<ModInt<P7>>(quotientDegrees, quotientCoefficients);
        var remainder = new SparsePolynomialBuilder<ModInt<P7>>(remainderDegrees, remainderCoefficients);

        var status = SparsePolynomialKernels.TryDivMod(
            dividend,
            divisor,
            ref quotient,
            ref remainder,
            quotientWorkspace,
            remainderWorkspace,
            new ModIntOps<P7>());

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.Equal(6, quotient.AsView().CoefficientAt(0).Value);
        Assert.Equal(1, quotient.AsView().CoefficientAt(1).Value);
        Assert.Equal(2, remainder.AsView().CoefficientAt(0).Value);
    }

    [Fact]
    public void Rational32Kernels_NormalizeAndReportOverflowOrDivisionByZero()
    {
        Assert.Equal(AlgebraStatus.Ok, Rational32Kernels.TryCreate(2, 4, out var half));
        Assert.Equal(1, half.Numerator);
        Assert.Equal(2, half.Denominator);

        Assert.Equal(AlgebraStatus.Ok, Rational32Kernels.TryCreate(-6, -8, out var threeFourths));
        Assert.Equal(3, threeFourths.Numerator);
        Assert.Equal(4, threeFourths.Denominator);

        Assert.Equal(AlgebraStatus.Ok, Rational32Kernels.TryAdd(half, threeFourths, out var sum));
        Assert.Equal(5, sum.Numerator);
        Assert.Equal(4, sum.Denominator);

        Assert.Equal(AlgebraStatus.Ok, Rational32Kernels.TryMul(half, threeFourths, out var product));
        Assert.Equal(3, product.Numerator);
        Assert.Equal(8, product.Denominator);

        Assert.Equal(AlgebraStatus.DivisionByZero, Rational32Kernels.TryCreate(1, 0, out _));
        Assert.Equal(
            AlgebraStatus.Overflow,
            Rational32Kernels.TryAdd(
                new Rational32(int.MaxValue, 1),
                new Rational32(1, 1),
                out _));
    }

    [Fact]
    public void Rational32StatusOps_ReportPolynomialOverflowAsStatus()
    {
        ReadOnlySpan<int> leftDegrees = [0];
        ReadOnlySpan<Rational32> leftCoefficients = [new(int.MaxValue, 1)];
        ReadOnlySpan<int> rightDegrees = [0];
        ReadOnlySpan<Rational32> rightCoefficients = [new(1, 1)];
        var left = new SparsePolynomialView<Rational32>(
            new FinsuppView<int, Rational32>(leftDegrees, leftCoefficients));
        var right = new SparsePolynomialView<Rational32>(
            new FinsuppView<int, Rational32>(rightDegrees, rightCoefficients));

        Span<int> destinationDegrees = stackalloc int[1];
        Span<Rational32> destinationCoefficients = stackalloc Rational32[1];
        var destination = new SparsePolynomialBuilder<Rational32>(destinationDegrees, destinationCoefficients);

        var status = StatusSparsePolynomialKernels.TryAdd(
            left,
            right,
            ref destination,
            new Rational32StatusFieldOps());

        Assert.Equal(AlgebraStatus.Overflow, status);
        Assert.Equal(0, destination.Count);
    }

    [Fact]
    public void Padic32Ops_ModelTruncatedResiduesModuloPrimePower()
    {
        Assert.Equal(AlgebraStatus.Ok, Padic32Kernels.TryGetModulus<P7, N3>(out var modulus));
        Assert.Equal(343, modulus);

        var ops = new Padic32Ops<P7, N3>();
        var left = new Padic32<P7, N3>(340);
        var right = new Padic32<P7, N3>(10);
        var result = Padic32<P7, N3>.Zero;

        Assert.Equal(AlgebraStatus.Ok, ops.TryAdd(ref result, left, right));
        Assert.Equal(7, result.Value);

        Assert.Equal(AlgebraStatus.Ok, ops.TryMul(ref result, new Padic32<P7, N3>(50), new Padic32<P7, N3>(8)));
        Assert.Equal(57, result.Value);

        var divisible = new Padic32<P7, N3>(98);
        Assert.False(divisible.IsUnit);
        Assert.Equal(AlgebraStatus.Ok, divisible.TryValuation(out var valuation));
        Assert.Equal(2, valuation);

        var unit = new Padic32<P7, N3>(10);
        Assert.True(unit.IsUnit);
        Assert.Equal(AlgebraStatus.Ok, unit.TryValuation(out valuation));
        Assert.Equal(0, valuation);

        Assert.Equal(AlgebraStatus.Ok, ops.TryInvert(ref result, unit));
        Assert.Equal(AlgebraStatus.Ok, ops.TryMul(ref result, result, unit));
        Assert.Equal(1, result.Value);

        Assert.Equal(AlgebraStatus.NonInvertible, ops.TryInvert(ref result, divisible));
        Assert.Equal(343, ops.Cardinality);

        Span<Padic32<P7, N3>> residues = stackalloc Padic32<P7, N3>[343];
        Assert.Equal(AlgebraStatus.Ok, ops.TryFill(residues));
        Assert.Equal(0, residues[0].Value);
        Assert.Equal(342, residues[342].Value);
    }

    [Fact]
    public void WittVectorKernels_PreserveGhostArithmeticForLengthTwo()
    {
        ReadOnlySpan<int> leftComponents = [1, 2];
        ReadOnlySpan<int> rightComponents = [3, 4];
        var left = new WittVectorView<int>(leftComponents);
        var right = new WittVectorView<int>(rightComponents);
        var ops = new CheckedInt32RingOps();

        Span<int> sumComponents = stackalloc int[2];
        var sum = new WittVectorBuilder<int>(sumComponents);
        var status = WittVectorKernels.TryAdd<P2, N2, int, CheckedInt32RingOps, CheckedInt32RingOps>(
            left,
            right,
            ref sum,
            ops,
            ops);

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.True(sum.WrittenSpan.SequenceEqual([4, 3]));

        var leftGhost = 0;
        var rightGhost = 0;
        var sumGhost = 0;
        Assert.Equal(
            AlgebraStatus.Ok,
            WittVectorKernels.TryGhostComponent<P2, N2, int, CheckedInt32RingOps, CheckedInt32RingOps>(
                left,
                1,
                ref leftGhost,
                ops,
                ops));
        Assert.Equal(
            AlgebraStatus.Ok,
            WittVectorKernels.TryGhostComponent<P2, N2, int, CheckedInt32RingOps, CheckedInt32RingOps>(
                right,
                1,
                ref rightGhost,
                ops,
                ops));
        Assert.Equal(
            AlgebraStatus.Ok,
            WittVectorKernels.TryGhostComponent<P2, N2, int, CheckedInt32RingOps, CheckedInt32RingOps>(
                sum.AsView(),
                1,
                ref sumGhost,
                ops,
                ops));
        Assert.Equal(leftGhost + rightGhost, sumGhost);

        Span<int> productComponents = stackalloc int[2];
        var product = new WittVectorBuilder<int>(productComponents);
        status = WittVectorKernels.TryMul<P2, N2, int, CheckedInt32RingOps, CheckedInt32RingOps>(
            left,
            right,
            ref product,
            ops,
            ops);

        Assert.Equal(AlgebraStatus.Ok, status);
        Assert.True(product.WrittenSpan.SequenceEqual([3, 38]));

        var productGhost = 0;
        Assert.Equal(
            AlgebraStatus.Ok,
            WittVectorKernels.TryGhostComponent<P2, N2, int, CheckedInt32RingOps, CheckedInt32RingOps>(
                product.AsView(),
                1,
                ref productGhost,
                ops,
                ops));
        Assert.Equal(leftGhost * rightGhost, productGhost);

        Span<int> negativeComponents = stackalloc int[2];
        var negative = new WittVectorBuilder<int>(negativeComponents);
        Assert.Equal(
            AlgebraStatus.Ok,
            WittVectorKernels.TryNeg<P2, N2, int, CheckedInt32RingOps, CheckedInt32RingOps>(
                left,
                ref negative,
                ops,
                ops));
        Assert.True(negative.WrittenSpan.SequenceEqual([-1, -3]));

        Span<int> zeroComponents = stackalloc int[2];
        var zero = new WittVectorBuilder<int>(zeroComponents);
        Assert.Equal(
            AlgebraStatus.Ok,
            WittVectorKernels.TryAdd<P2, N2, int, CheckedInt32RingOps, CheckedInt32RingOps>(
                left,
                negative.AsView(),
                ref zero,
                ops,
                ops));
        Assert.True(zero.WrittenSpan.SequenceEqual([0, 0]));
    }

    [Fact]
    public void WittVectorKernels_AllowLengthThreeValuesButGateUnsupportedArithmetic()
    {
        ReadOnlySpan<int> leftComponents = [1, 2, 3];
        ReadOnlySpan<int> rightComponents = [4, 5, 6];
        var left = new WittVectorView<int>(leftComponents);
        var right = new WittVectorView<int>(rightComponents);
        var ops = new CheckedInt32RingOps();

        Span<int> zeroComponents = stackalloc int[3];
        var zero = new WittVectorBuilder<int>(zeroComponents);
        Assert.Equal(
            AlgebraStatus.Ok,
            WittVectorKernels.TryZero<P2, N3, int, CheckedInt32RingOps>(ref zero, ops));
        Assert.True(zero.WrittenSpan.SequenceEqual([0, 0, 0]));

        var ghost0 = 0;
        Assert.Equal(
            AlgebraStatus.Ok,
            WittVectorKernels.TryGhostComponent<P2, N3, int, CheckedInt32RingOps, CheckedInt32RingOps>(
                left,
                0,
                ref ghost0,
                ops,
                ops));
        Assert.Equal(1, ghost0);

        var ghost2 = 0;
        Assert.Equal(
            AlgebraStatus.InvalidInput,
            WittVectorKernels.TryGhostComponent<P2, N3, int, CheckedInt32RingOps, CheckedInt32RingOps>(
                left,
                2,
                ref ghost2,
                ops,
                ops));

        Span<int> sumComponents = stackalloc int[3];
        var sum = new WittVectorBuilder<int>(sumComponents);
        Assert.Equal(
            AlgebraStatus.InvalidInput,
            WittVectorKernels.TryAdd<P2, N3, int, CheckedInt32RingOps, CheckedInt32RingOps>(
                left,
                right,
                ref sum,
                ops,
                ops));
        Assert.Equal(0, sum.Written);
    }
}
