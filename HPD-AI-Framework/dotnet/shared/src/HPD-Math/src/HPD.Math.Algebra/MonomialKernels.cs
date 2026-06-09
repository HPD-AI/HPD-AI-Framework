using HPD.Math.Core;

namespace HPD.Math.Algebra;

/// <summary>
/// Allocation-free kernels over fixed-variable monomial exponent vectors.
/// </summary>
public static class MonomialKernels
{
    public static AlgebraStatus ValidateSameShape(ReadOnlySpan<int> left, ReadOnlySpan<int> right)
    {
        if (left.Length != right.Length)
            return AlgebraStatus.DimensionMismatch;

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] < 0 || right[i] < 0)
                return AlgebraStatus.InvalidInput;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryTotalDegree(ReadOnlySpan<int> exponents, out int totalDegree)
    {
        totalDegree = 0;
        for (var i = 0; i < exponents.Length; i++)
        {
            if (exponents[i] < 0)
                return AlgebraStatus.InvalidInput;

            var next = (long)totalDegree + exponents[i];
            if (next > int.MaxValue)
                return AlgebraStatus.Overflow;

            totalDegree = (int)next;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryMul(
        ReadOnlySpan<int> left,
        ReadOnlySpan<int> right,
        Span<int> destination)
    {
        var status = ValidateSameShape(left, right);
        if (status != AlgebraStatus.Ok)
            return status;
        if (destination.Length < left.Length)
            return AlgebraStatus.InsufficientDestination;

        for (var i = 0; i < left.Length; i++)
        {
            var sum = (long)left[i] + right[i];
            if (sum > int.MaxValue)
                return AlgebraStatus.Overflow;

            destination[i] = (int)sum;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryDivides(
        ReadOnlySpan<int> divisor,
        ReadOnlySpan<int> dividend,
        out bool divides)
    {
        divides = false;
        var status = ValidateSameShape(divisor, dividend);
        if (status != AlgebraStatus.Ok)
            return status;

        for (var i = 0; i < divisor.Length; i++)
        {
            if (divisor[i] > dividend[i])
                return AlgebraStatus.Ok;
        }

        divides = true;
        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryDiv(
        ReadOnlySpan<int> dividend,
        ReadOnlySpan<int> divisor,
        Span<int> destination)
    {
        var status = ValidateSameShape(dividend, divisor);
        if (status != AlgebraStatus.Ok)
            return status;
        if (destination.Length < dividend.Length)
            return AlgebraStatus.InsufficientDestination;

        for (var i = 0; i < dividend.Length; i++)
        {
            if (divisor[i] > dividend[i])
                return AlgebraStatus.InvalidInput;

            destination[i] = dividend[i] - divisor[i];
        }

        return AlgebraStatus.Ok;
    }
}
