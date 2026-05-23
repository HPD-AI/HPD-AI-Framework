namespace Helium.Primitives;

/// <summary>
/// Executable finite enumeration for a type.
/// </summary>
public interface IFintype<T>
    where T : IDecidableEq<T>
{
    static abstract FiniteList<T> Elements { get; }
    static abstract int Cardinality { get; }
}
