namespace Helium.Primitives;

public interface IBoundedLattice<T> : ILattice<T>
{
    static abstract T Top { get; }
    static abstract T Bottom { get; }
}
