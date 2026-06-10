using HPD.Math.Core;

namespace HPD.Math.Algebra;

/// <summary>
/// Non-owning dense univariate polynomial view. Coefficients are stored from degree 0 upward.
/// Canonical dense views have no trailing zero coefficient unless they are empty.
/// </summary>
public readonly ref struct DensePolynomialView<TCoefficient>
{
    public DensePolynomialView(ReadOnlySpan<TCoefficient> coefficients)
    {
        Coefficients = coefficients;
    }

    public ReadOnlySpan<TCoefficient> Coefficients { get; }

    public int CoefficientCount => Coefficients.Length;

    public bool IsZero => CoefficientCount == 0;

    public int Degree => CoefficientCount - 1;

    public TCoefficient CoefficientAt(int degree) =>
        degree < 0 || degree >= CoefficientCount ? default! : Coefficients[degree];
}

public static class DensePolynomialViewExtensions
{
    extension<TCoefficient>(DensePolynomialView<TCoefficient> self)
    {
        public AlgebraStatus ValidateCanonical<TCoefficientOps>(TCoefficientOps coefficientOps)
            where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
        {
            return DensePolynomialKernels.ValidateCanonical(self, coefficientOps);
        }
    }
}
