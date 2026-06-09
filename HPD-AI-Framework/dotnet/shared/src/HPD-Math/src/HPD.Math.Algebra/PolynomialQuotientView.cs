using HPD.Math.Core;

namespace HPD.Math.Algebra;

/// <summary>
/// Non-owning view of a univariate polynomial residue representative.
/// The modulus is supplied explicitly to kernels as quotient-ring context.
/// </summary>
public readonly ref struct PolynomialQuotientView<TCoefficient>
{
    public PolynomialQuotientView(SparsePolynomialView<TCoefficient> representative)
    {
        Representative = representative;
    }

    public SparsePolynomialView<TCoefficient> Representative { get; }

    public bool IsZero => Representative.IsZero;
}

public static class PolynomialQuotientViewExtensions
{
    extension<TCoefficient>(PolynomialQuotientView<TCoefficient> self)
    {
        public AlgebraStatus Validate<TCoefficientOps>(TCoefficientOps coefficientOps)
            where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
        {
            return self.Representative.ValidateCanonical(coefficientOps);
        }

        public AlgebraStatus ValidateStatus<TCoefficientOps>(TCoefficientOps coefficientOps)
            where TCoefficientOps : struct, IStatusRingOps<TCoefficient>
        {
            return self.Representative.ValidateCanonicalStatus(coefficientOps);
        }
    }
}
