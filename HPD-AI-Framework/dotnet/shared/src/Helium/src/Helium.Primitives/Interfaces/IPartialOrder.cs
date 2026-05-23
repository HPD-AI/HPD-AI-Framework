namespace Helium.Primitives;

/// <summary>
/// Partial order with decidable equality.
/// </summary>
public interface IPartialOrder<T> : IPreorder<T>, IDecidableEq<T>
{
}
