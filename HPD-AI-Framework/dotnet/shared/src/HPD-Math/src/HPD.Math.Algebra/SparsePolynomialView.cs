using HPD.Math.Core;
using HPD.Math.Finite;

namespace HPD.Math.Algebra;

/// <summary>
/// Non-owning sparse univariate polynomial view. Terms are canonical when degrees are strictly increasing
/// and zero coefficients are omitted.
/// </summary>
public readonly ref struct SparsePolynomialView<TCoefficient>
{
    public SparsePolynomialView(FinsuppView<int, TCoefficient> terms)
    {
        Terms = terms;
    }

    public FinsuppView<int, TCoefficient> Terms { get; }

    public int TermCount => Terms.Count;

    public bool IsZero => Terms.IsZero;

    public int Degree => IsZero ? -1 : Terms.Keys[^1];

    public int DegreeAt(int supportIndex) => Terms.Keys[supportIndex];

    public TCoefficient CoefficientAt(int supportIndex) => Terms.Values[supportIndex];
}

public static class SparsePolynomialViewExtensions
{
    extension<TCoefficient>(SparsePolynomialView<TCoefficient> self)
    {
        public AlgebraStatus ValidateCanonical<TCoefficientOps>(TCoefficientOps coefficientOps)
            where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
        {
            return self.Terms.ValidateCanonical(new Int32OrderOps(), coefficientOps);
        }

        public AlgebraStatus ValidateCanonicalStatus<TCoefficientOps>(TCoefficientOps coefficientOps)
            where TCoefficientOps : struct, IStatusRingOps<TCoefficient>
        {
            return self.Terms.ValidateCanonicalStatus(new Int32OrderOps(), coefficientOps);
        }
    }
}
