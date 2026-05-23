using System.Numerics;

namespace Helium.Primitives;

/// <summary>
/// An additive group: a type with addition, subtraction, negation, zero, and decidable equality.
/// </summary>
public interface IAddGroup<T> :
    IAdditionOperators<T, T, T>,
    ISubtractionOperators<T, T, T>,
    IUnaryNegationOperators<T, T>,
    IAdditiveIdentity<T, T>,
    IDecidableEq<T>
    where T : IAddGroup<T>
{
}
