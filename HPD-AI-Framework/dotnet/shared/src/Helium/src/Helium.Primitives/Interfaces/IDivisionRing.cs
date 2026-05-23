namespace Helium.Primitives;

/// <summary>
/// A ring where every nonzero element has a multiplicative inverse.
/// Multiplication is not required to be commutative.
/// </summary>
public interface IDivisionRing<T> : IRing<T>
    where T : IDivisionRing<T>
{
    static abstract T Invert(T value);
}
