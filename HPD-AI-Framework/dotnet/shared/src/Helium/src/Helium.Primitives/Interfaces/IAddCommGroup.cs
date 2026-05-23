namespace Helium.Primitives;

/// <summary>
/// A commutative additive group.
/// </summary>
public interface IAddCommGroup<T> : IAddGroup<T>
    where T : IAddCommGroup<T>
{
}
