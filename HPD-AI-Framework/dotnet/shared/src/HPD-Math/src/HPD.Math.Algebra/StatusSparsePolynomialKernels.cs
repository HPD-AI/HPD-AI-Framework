using HPD.Math.Core;

namespace HPD.Math.Algebra;

/// <summary>
/// Sparse polynomial kernels for bounded coefficient rings with status-returning arithmetic.
/// </summary>
public static class StatusSparsePolynomialKernels
{
    public static AlgebraStatus TryAdd<TCoefficient, TCoefficientOps>(
        SparsePolynomialView<TCoefficient> left,
        SparsePolynomialView<TCoefficient> right,
        ref SparsePolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IStatusRingOps<TCoefficient>
    {
        var status = left.ValidateCanonicalStatus(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        status = right.ValidateCanonicalStatus(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();

        var i = 0;
        var j = 0;
        while (i < left.TermCount && j < right.TermCount)
        {
            var leftDegree = left.DegreeAt(i);
            var rightDegree = right.DegreeAt(j);

            if (leftDegree < rightDegree)
            {
                status = destination.TryAppendTermStatus(leftDegree, left.CoefficientAt(i), coefficientOps);
                i++;
            }
            else if (leftDegree > rightDegree)
            {
                status = destination.TryAppendTermStatus(rightDegree, right.CoefficientAt(j), coefficientOps);
                j++;
            }
            else
            {
                var sum = coefficientOps.Zero;
                status = coefficientOps.TryAdd(ref sum, left.CoefficientAt(i), right.CoefficientAt(j));
                if (status != AlgebraStatus.Ok)
                    return status;

                status = destination.TryAppendTermStatus(leftDegree, sum, coefficientOps);
                i++;
                j++;
            }

            if (status != AlgebraStatus.Ok)
                return status;
        }

        while (i < left.TermCount)
        {
            status = destination.TryAppendTermStatus(left.DegreeAt(i), left.CoefficientAt(i), coefficientOps);
            if (status != AlgebraStatus.Ok)
                return status;
            i++;
        }

        while (j < right.TermCount)
        {
            status = destination.TryAppendTermStatus(right.DegreeAt(j), right.CoefficientAt(j), coefficientOps);
            if (status != AlgebraStatus.Ok)
                return status;
            j++;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryMul<TCoefficient, TCoefficientOps>(
        SparsePolynomialView<TCoefficient> left,
        SparsePolynomialView<TCoefficient> right,
        ref SparsePolynomialBuilder<TCoefficient> destination,
        Span<int> workspaceDegrees,
        Span<TCoefficient> workspaceCoefficients,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IStatusRingOps<TCoefficient>
    {
        var status = left.ValidateCanonicalStatus(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        status = right.ValidateCanonicalStatus(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        if (left.IsZero || right.IsZero)
            return AlgebraStatus.Ok;

        var maxProducts = checked(left.TermCount * right.TermCount);
        if (workspaceDegrees.Length < maxProducts || workspaceCoefficients.Length < maxProducts)
            return AlgebraStatus.InsufficientWorkspace;

        var workspaceCount = 0;
        for (var i = 0; i < left.TermCount; i++)
        {
            for (var j = 0; j < right.TermCount; j++)
            {
                var degree = checked(left.DegreeAt(i) + right.DegreeAt(j));
                var product = coefficientOps.Zero;
                status = coefficientOps.TryMul(ref product, left.CoefficientAt(i), right.CoefficientAt(j));
                if (status != AlgebraStatus.Ok)
                    return status;

                status = Accumulate(degree, product, workspaceDegrees, workspaceCoefficients, ref workspaceCount, coefficientOps);
                if (status != AlgebraStatus.Ok)
                    return status;
            }
        }

        SortByDegree(workspaceDegrees[..workspaceCount], workspaceCoefficients[..workspaceCount]);

        for (var i = 0; i < workspaceCount; i++)
        {
            status = destination.TryAppendTermStatus(workspaceDegrees[i], workspaceCoefficients[i], coefficientOps);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    private static AlgebraStatus Accumulate<TCoefficient, TCoefficientOps>(
        int degree,
        in TCoefficient coefficient,
        Span<int> degrees,
        Span<TCoefficient> coefficients,
        ref int count,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IStatusRingOps<TCoefficient>
    {
        for (var i = 0; i < count; i++)
        {
            if (degrees[i] != degree)
                continue;

            var status = coefficientOps.TryAdd(ref coefficients[i], coefficients[i], coefficient);
            if (status != AlgebraStatus.Ok)
                return status;
            return AlgebraStatus.Ok;
        }

        degrees[count] = degree;
        coefficients[count] = coefficient;
        count++;
        return AlgebraStatus.Ok;
    }

    private static void SortByDegree<TCoefficient>(Span<int> degrees, Span<TCoefficient> coefficients)
    {
        for (var i = 1; i < degrees.Length; i++)
        {
            var degree = degrees[i];
            var coefficient = coefficients[i];
            var j = i - 1;
            while (j >= 0 && degrees[j] > degree)
            {
                degrees[j + 1] = degrees[j];
                coefficients[j + 1] = coefficients[j];
                j--;
            }

            degrees[j + 1] = degree;
            coefficients[j + 1] = coefficient;
        }
    }

}
