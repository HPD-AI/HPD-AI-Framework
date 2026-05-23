namespace Helium.Primitives;

public interface IBooleanAlgebra<T> : IDistributiveLattice<T>, IBoundedLattice<T>
{
    static abstract T Complement(T value);
}
