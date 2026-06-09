using HPD.Math.Core;

namespace HPD.Math.Algebra;

/// <summary>
/// Executable monomial order over fixed-width exponent vectors.
/// </summary>
public interface IMonomialOrderOps
{
    Ordering Compare(ReadOnlySpan<int> left, ReadOnlySpan<int> right);
}

/// <summary>
/// Lexicographic monomial order.
/// </summary>
public readonly struct LexMonomialOrderOps : IMonomialOrderOps
{
    public Ordering Compare(ReadOnlySpan<int> left, ReadOnlySpan<int> right)
    {
        var status = MonomialKernels.ValidateSameShape(left, right);
        if (status != AlgebraStatus.Ok)
            return Ordering.Equal;

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] < right[i])
                return Ordering.Less;
            if (left[i] > right[i])
                return Ordering.Greater;
        }

        return Ordering.Equal;
    }
}

/// <summary>
/// Graded lexicographic monomial order.
/// </summary>
public readonly struct GradedLexMonomialOrderOps : IMonomialOrderOps
{
    public Ordering Compare(ReadOnlySpan<int> left, ReadOnlySpan<int> right)
    {
        var status = MonomialKernels.TryTotalDegree(left, out var leftDegree);
        if (status != AlgebraStatus.Ok)
            return Ordering.Equal;

        status = MonomialKernels.TryTotalDegree(right, out var rightDegree);
        if (status != AlgebraStatus.Ok)
            return Ordering.Equal;

        if (leftDegree < rightDegree)
            return Ordering.Less;
        if (leftDegree > rightDegree)
            return Ordering.Greater;

        return new LexMonomialOrderOps().Compare(left, right);
    }
}

/// <summary>
/// Graded reverse lexicographic monomial order.
/// </summary>
public readonly struct GradedReverseLexMonomialOrderOps : IMonomialOrderOps
{
    public Ordering Compare(ReadOnlySpan<int> left, ReadOnlySpan<int> right)
    {
        var status = MonomialKernels.TryTotalDegree(left, out var leftDegree);
        if (status != AlgebraStatus.Ok)
            return Ordering.Equal;

        status = MonomialKernels.TryTotalDegree(right, out var rightDegree);
        if (status != AlgebraStatus.Ok)
            return Ordering.Equal;

        if (leftDegree < rightDegree)
            return Ordering.Less;
        if (leftDegree > rightDegree)
            return Ordering.Greater;

        status = MonomialKernels.ValidateSameShape(left, right);
        if (status != AlgebraStatus.Ok)
            return Ordering.Equal;

        for (var i = left.Length - 1; i >= 0; i--)
        {
            if (left[i] < right[i])
                return Ordering.Greater;
            if (left[i] > right[i])
                return Ordering.Less;
        }

        return Ordering.Equal;
    }
}
