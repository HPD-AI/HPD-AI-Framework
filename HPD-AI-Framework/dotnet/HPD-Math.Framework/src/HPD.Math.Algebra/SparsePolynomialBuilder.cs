using HPD.Math.Core;
using HPD.Math.Finite;

namespace HPD.Math.Algebra;

/// <summary>
/// Caller-owned sparse polynomial builder.
/// </summary>
public ref struct SparsePolynomialBuilder<TCoefficient>
{
    private FinsuppBuilder<int, TCoefficient> _terms;

    public SparsePolynomialBuilder(Span<int> degrees, Span<TCoefficient> coefficients)
    {
        _terms = new FinsuppBuilder<int, TCoefficient>(degrees, coefficients);
    }

    public int Count => _terms.Count;

    public void Clear() => _terms.Clear();

    public AlgebraStatus TryAppendTerm<TCoefficientOps>(
        int degree,
        in TCoefficient coefficient,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        if (degree < 0)
            return AlgebraStatus.InvalidInput;

        return _terms.TryAppendCanonical(degree, coefficient, new Int32OrderOps(), coefficientOps);
    }

    public AlgebraStatus TryAppendTermStatus<TCoefficientOps>(
        int degree,
        in TCoefficient coefficient,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IStatusRingOps<TCoefficient>
    {
        if (degree < 0)
            return AlgebraStatus.InvalidInput;

        return _terms.TryAppendCanonicalStatus(degree, coefficient, new Int32OrderOps(), coefficientOps);
    }

    public SparsePolynomialView<TCoefficient> AsView() =>
        new(_terms.AsView());
}
