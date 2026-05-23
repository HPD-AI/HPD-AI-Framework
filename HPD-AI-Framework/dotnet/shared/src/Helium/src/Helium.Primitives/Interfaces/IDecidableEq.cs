namespace Helium.Primitives;

/// <summary>
/// Type whose equality is decidable by an executable Helium procedure.
/// </summary>
public interface IDecidableEq<T>
{
    static abstract bool DecidableEquals(T left, T right);
}
