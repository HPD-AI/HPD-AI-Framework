namespace Helium.Primitives;

/// <summary>
/// Reflexive and transitive relation.
/// </summary>
public interface IPreorder<T>
{
    static abstract bool LessEqual(T left, T right);
}
