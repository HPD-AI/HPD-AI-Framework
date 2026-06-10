using HPD.Math.Core;

namespace HPD.Math.Algebra;

/// <summary>
/// Non-owning univariate rational function view: numerator / denominator.
/// </summary>
public readonly ref struct RationalFunctionView<TCoefficient>
{
    public RationalFunctionView(
        SparsePolynomialView<TCoefficient> numerator,
        SparsePolynomialView<TCoefficient> denominator)
    {
        Numerator = numerator;
        Denominator = denominator;
    }

    public SparsePolynomialView<TCoefficient> Numerator { get; }

    public SparsePolynomialView<TCoefficient> Denominator { get; }

    public bool IsZero => Numerator.IsZero;
}

public static class RationalFunctionViewExtensions
{
    extension<TCoefficient>(RationalFunctionView<TCoefficient> self)
    {
        public AlgebraStatus Validate<TCoefficientOps>(TCoefficientOps coefficientOps)
            where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
        {
            return RationalFunctionKernels.Validate(self, coefficientOps);
        }
    }
}
