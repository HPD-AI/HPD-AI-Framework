using HPD.Math.Core;

namespace HPD.Math.Algebra;

/// <summary>
/// Non-owning monomial view. Exponents are stored by variable index.
/// </summary>
public readonly ref struct MonomialView
{
    public MonomialView(ReadOnlySpan<int> exponents)
    {
        Exponents = exponents;
    }

    public ReadOnlySpan<int> Exponents { get; }

    public int VariableCount => Exponents.Length;

    public int this[int variableIndex] => Exponents[variableIndex];

    public AlgebraStatus ValidateShape()
    {
        for (var i = 0; i < Exponents.Length; i++)
        {
            if (Exponents[i] < 0)
                return AlgebraStatus.InvalidInput;
        }

        return AlgebraStatus.Ok;
    }
}
