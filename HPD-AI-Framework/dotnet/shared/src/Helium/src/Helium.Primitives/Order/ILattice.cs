namespace Helium.Primitives;

public interface ILattice<T> : IPartialOrder<T>
{
    static abstract T Join(T left, T right);
    static abstract T Meet(T left, T right);
}
