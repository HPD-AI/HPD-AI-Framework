using System.Numerics;

namespace Helium.Primitives;

/// <summary>
/// Commutative additive monoid: addition with an additive identity, but no inverse requirement.
/// </summary>
public interface IAddCommMonoid<T> :
    IAdditionOperators<T, T, T>,
    IAdditiveIdentity<T, T>,
    IDecidableEq<T>
    where T : IAddCommMonoid<T>
{
}
