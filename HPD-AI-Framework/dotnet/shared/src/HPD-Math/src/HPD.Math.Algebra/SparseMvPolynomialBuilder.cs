using HPD.Math.Core;

namespace HPD.Math.Algebra;

/// <summary>
/// Caller-owned builder for canonical sparse multivariate polynomial terms.
/// </summary>
public ref struct SparseMvPolynomialBuilder<TCoefficient>
{
    private readonly Span<int> _exponents;
    private readonly Span<TCoefficient> _coefficients;
    private readonly int _variableCount;
    private int _count;

    public SparseMvPolynomialBuilder(int variableCount, Span<int> exponents, Span<TCoefficient> coefficients)
    {
        _exponents = exponents;
        _coefficients = coefficients;
        _variableCount = variableCount;
        _count = 0;
    }

    public int VariableCount => _variableCount;

    public int Count => _count;

    public int Capacity => _variableCount <= 0
        ? (_variableCount == 0 ? _coefficients.Length : 0)
        : System.Math.Min(_coefficients.Length, _exponents.Length / _variableCount);

    public void Clear() => _count = 0;

    public AlgebraStatus TryAppendTerm<TCoefficientOps, TMonomialOrder>(
        ReadOnlySpan<int> monomial,
        in TCoefficient coefficient,
        TCoefficientOps coefficientOps,
        TMonomialOrder monomialOrder)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
        where TMonomialOrder : struct, IMonomialOrderOps
    {
        if (_variableCount < 0)
            return AlgebraStatus.InvalidInput;
        if (monomial.Length != _variableCount)
            return AlgebraStatus.DimensionMismatch;
        if (coefficientOps.Eq(coefficient, coefficientOps.Zero))
            return AlgebraStatus.Ok;
        if (_count >= Capacity)
            return AlgebraStatus.InsufficientDestination;

        for (var i = 0; i < monomial.Length; i++)
        {
            if (monomial[i] < 0)
                return AlgebraStatus.InvalidInput;
        }

        if (_count > 0 && monomialOrder.Compare(MonomialAt(_count - 1), monomial) != Ordering.Less)
            return AlgebraStatus.InvalidInput;

        monomial.CopyTo(_exponents.Slice(_count * _variableCount, _variableCount));
        _coefficients[_count] = coefficient;
        _count++;
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TryAppendConstant<TCoefficientOps, TMonomialOrder>(
        in TCoefficient coefficient,
        TCoefficientOps coefficientOps,
        TMonomialOrder monomialOrder)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
        where TMonomialOrder : struct, IMonomialOrderOps
    {
        if (_variableCount < 0)
            return AlgebraStatus.InvalidInput;
        if (coefficientOps.Eq(coefficient, coefficientOps.Zero))
            return AlgebraStatus.Ok;
        if (_count >= Capacity)
            return AlgebraStatus.InsufficientDestination;

        var target = _exponents.Slice(_count * _variableCount, _variableCount);
        target.Clear();

        if (_count > 0 && monomialOrder.Compare(MonomialAt(_count - 1), target) != Ordering.Less)
            return AlgebraStatus.InvalidInput;

        _coefficients[_count] = coefficient;
        _count++;
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TryAppendVariable<TCoefficientOps, TMonomialOrder>(
        int variableIndex,
        in TCoefficient coefficient,
        TCoefficientOps coefficientOps,
        TMonomialOrder monomialOrder)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
        where TMonomialOrder : struct, IMonomialOrderOps
    {
        if (_variableCount <= 0 || variableIndex < 0 || variableIndex >= _variableCount)
            return AlgebraStatus.InvalidInput;
        if (coefficientOps.Eq(coefficient, coefficientOps.Zero))
            return AlgebraStatus.Ok;
        if (_count >= Capacity)
            return AlgebraStatus.InsufficientDestination;

        var target = _exponents.Slice(_count * _variableCount, _variableCount);
        target.Clear();
        target[variableIndex] = 1;

        if (_count > 0 && monomialOrder.Compare(MonomialAt(_count - 1), target) != Ordering.Less)
            return AlgebraStatus.InvalidInput;

        _coefficients[_count] = coefficient;
        _count++;
        return AlgebraStatus.Ok;
    }

    public SparseMvPolynomialView<TCoefficient> AsView() =>
        new(_variableCount, _exponents[..(_count * _variableCount)], _coefficients[.._count]);

    private ReadOnlySpan<int> MonomialAt(int supportIndex) =>
        _exponents.Slice(supportIndex * _variableCount, _variableCount);
}
