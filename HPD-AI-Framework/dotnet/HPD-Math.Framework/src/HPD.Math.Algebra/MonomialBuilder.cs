using HPD.Math.Core;

namespace HPD.Math.Algebra;

/// <summary>
/// Caller-owned builder for fixed-variable monomial exponents.
/// </summary>
public ref struct MonomialBuilder
{
    private readonly Span<int> _exponents;
    private int _count;

    public MonomialBuilder(Span<int> exponents)
    {
        _exponents = exponents;
        _count = 0;
    }

    public int Count => _count;

    public int Capacity => _exponents.Length;

    public void Clear() => _count = 0;

    public AlgebraStatus TrySet(ReadOnlySpan<int> exponents)
    {
        if (exponents.Length > Capacity)
            return AlgebraStatus.InsufficientDestination;

        for (var i = 0; i < exponents.Length; i++)
        {
            if (exponents[i] < 0)
                return AlgebraStatus.InvalidInput;

            _exponents[i] = exponents[i];
        }

        _count = exponents.Length;
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TrySetVariable(int variableCount, int variableIndex)
    {
        if (variableCount < 0 || variableIndex < 0 || variableIndex >= variableCount)
            return AlgebraStatus.InvalidInput;
        if (variableCount > Capacity)
            return AlgebraStatus.InsufficientDestination;

        _exponents[..variableCount].Clear();
        _exponents[variableIndex] = 1;
        _count = variableCount;
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TrySetOne(int variableCount)
    {
        if (variableCount < 0)
            return AlgebraStatus.InvalidInput;
        if (variableCount > Capacity)
            return AlgebraStatus.InsufficientDestination;

        _exponents[..variableCount].Clear();
        _count = variableCount;
        return AlgebraStatus.Ok;
    }

    public MonomialView AsView() => new(_exponents[.._count]);
}
