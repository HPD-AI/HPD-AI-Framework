namespace Helium.Primitives;

/// <summary>
/// A commutative ring: ring where a * b == b * a.
/// Marker interface — adds no members.
/// </summary>
public interface ICommRing<T> : IRing<T>
    where T : ICommRing<T>
{
}
