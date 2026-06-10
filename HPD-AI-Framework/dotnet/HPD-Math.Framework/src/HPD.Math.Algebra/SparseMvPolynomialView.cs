using HPD.Math.Core;

namespace HPD.Math.Algebra;

/// <summary>
/// Non-owning sparse multivariate polynomial view. Exponents are term-major:
/// term i occupies Exponents[(i * VariableCount)..((i + 1) * VariableCount)].
/// </summary>
public readonly ref struct SparseMvPolynomialView<TCoefficient>
{
    public SparseMvPolynomialView(int variableCount, ReadOnlySpan<int> exponents, ReadOnlySpan<TCoefficient> coefficients)
    {
        VariableCount = variableCount;
        Exponents = exponents;
        Coefficients = coefficients;
    }

    public int VariableCount { get; }

    public ReadOnlySpan<int> Exponents { get; }

    public ReadOnlySpan<TCoefficient> Coefficients { get; }

    public int TermCount => Coefficients.Length;

    public bool IsZero => TermCount == 0;

    public ReadOnlySpan<int> MonomialAt(int supportIndex) =>
        Exponents.Slice(supportIndex * VariableCount, VariableCount);

    public TCoefficient CoefficientAt(int supportIndex) => Coefficients[supportIndex];

    public AlgebraStatus ValidateShape()
    {
        if (VariableCount < 0)
            return AlgebraStatus.InvalidInput;

        var expectedExponentCount = (long)TermCount * VariableCount;
        if (expectedExponentCount > int.MaxValue)
            return AlgebraStatus.InvalidInput;

        return Exponents.Length == (int)expectedExponentCount
            ? AlgebraStatus.Ok
            : AlgebraStatus.InvalidInput;
    }
}

public static class SparseMvPolynomialViewExtensions
{
    extension<TCoefficient>(SparseMvPolynomialView<TCoefficient> self)
    {
        public AlgebraStatus ValidateCanonical<TCoefficientOps, TMonomialOrder>(
            TCoefficientOps coefficientOps,
            TMonomialOrder monomialOrder)
            where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
            where TMonomialOrder : struct, IMonomialOrderOps
        {
            return SparseMvPolynomialKernels.ValidateCanonical(self, coefficientOps, monomialOrder);
        }
    }
}
