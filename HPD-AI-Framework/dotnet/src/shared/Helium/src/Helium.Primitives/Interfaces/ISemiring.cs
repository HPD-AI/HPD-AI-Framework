using System.Numerics;

namespace Helium.Primitives;

/// <summary>
/// A semiring: additive commutative monoid + multiplicative monoid + distributivity.
/// </summary>
public interface ISemiring<T> :
    IAdditionOperators<T, T, T>,
    IMultiplyOperators<T, T, T>,
    IAdditiveIdentity<T, T>,
    IMultiplicativeIdentity<T, T>,
    IEqualityOperators<T, T, bool>,
    IEquatable<T>
    where T : ISemiring<T>
{
}
