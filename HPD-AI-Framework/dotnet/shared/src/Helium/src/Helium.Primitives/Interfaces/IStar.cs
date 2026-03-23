namespace Helium.Primitives;

/// <summary>
/// An involution a → a* satisfying (a*)* = a, (a + b)* = a* + b*, (a * b)* = b* * a*.
/// Used for complex conjugation, operator adjoints, *-algebras.
/// </summary>
public interface IStar<T>
    where T : IStar<T>
{
    static abstract T Star(T a);
}
