using HPD.Math.Core;

namespace HPD.Math.Algebra;

/// <summary>
/// Allocation-free kernels over dense univariate polynomial views.
/// </summary>
public static class DensePolynomialKernels
{
    public static AlgebraStatus ValidateCanonical<TCoefficient, TCoefficientOps>(
        DensePolynomialView<TCoefficient> polynomial,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        if (polynomial.IsZero)
            return AlgebraStatus.Ok;

        return coefficientOps.Eq(polynomial.Coefficients[^1], coefficientOps.Zero)
            ? AlgebraStatus.InvalidInput
            : AlgebraStatus.Ok;
    }

    public static int NormalizedLength<TCoefficient, TCoefficientOps>(
        ReadOnlySpan<TCoefficient> coefficients,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        var length = coefficients.Length;
        while (length > 0 && coefficientOps.Eq(coefficients[length - 1], coefficientOps.Zero))
            length--;

        return length;
    }

    public static AlgebraStatus TryFromSparse<TCoefficient, TCoefficientOps>(
        SparsePolynomialView<TCoefficient> source,
        ref DensePolynomialBuilder<TCoefficient> destination,
        Span<TCoefficient> workspace,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        var status = source.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        if (source.IsZero)
            return AlgebraStatus.Ok;

        var count = source.Degree + 1;
        if (workspace.Length < count)
            return AlgebraStatus.InsufficientWorkspace;

        for (var i = 0; i < count; i++)
            workspace[i] = coefficientOps.Zero;

        for (var i = 0; i < source.TermCount; i++)
            workspace[source.DegreeAt(i)] = source.CoefficientAt(i);

        return destination.TrySetFromDense(workspace[..count], coefficientOps);
    }

    public static AlgebraStatus TryToSparse<TCoefficient, TCoefficientOps>(
        DensePolynomialView<TCoefficient> source,
        ref SparsePolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        var status = ValidateCanonical(source, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        for (var i = 0; i < source.CoefficientCount; i++)
        {
            status = destination.TryAppendTerm(i, source.Coefficients[i], coefficientOps);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }
}
