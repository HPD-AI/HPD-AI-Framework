using HPD.Math.Core;

namespace HPD.Math.Algebra;

/// <summary>
/// Caller-owned builder for dense polynomial coefficients.
/// </summary>
public ref struct DensePolynomialBuilder<TCoefficient>
{
    private readonly Span<TCoefficient> _coefficients;
    private int _count;

    public DensePolynomialBuilder(Span<TCoefficient> coefficients)
    {
        _coefficients = coefficients;
        _count = 0;
    }

    public int Count => _count;

    public int Capacity => _coefficients.Length;

    public void Clear() => _count = 0;

    public AlgebraStatus TrySetFromDense<TCoefficientOps>(
        ReadOnlySpan<TCoefficient> coefficients,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        var count = DensePolynomialKernels.NormalizedLength(coefficients, coefficientOps);
        if (_coefficients.Length < count)
            return AlgebraStatus.InsufficientDestination;

        for (var i = 0; i < count; i++)
            _coefficients[i] = coefficients[i];

        _count = count;
        return AlgebraStatus.Ok;
    }

    public DensePolynomialView<TCoefficient> AsView() => new(_coefficients[.._count]);
}
