using System.Numerics;

namespace Helium.Primitives;

/// <summary>
/// A field: commutative ring where every nonzero element has a multiplicative inverse.
/// Convention: Invert(0) returns 0 (total function).
/// </summary>
public interface IField<T> :
    ICommRing<T>,
    IDivisionOperators<T, T, T>
    where T : IField<T>
{
    static abstract T Invert(T a);
}
