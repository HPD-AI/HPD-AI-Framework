namespace Helium.Primitives;

/// <summary>
/// Executable finite completeness over finite lists.
/// </summary>
public interface ICompleteLattice<T> : IBoundedLattice<T>
    where T : IDecidableEq<T>
{
    static abstract T Supremum(FiniteList<T> values);
    static abstract T Infimum(FiniteList<T> values);
}
