namespace Helium.Primitives;

/// <summary>
/// Total order with a Helium-owned comparison operation.
/// </summary>
public interface ITotalOrder<T> : IPartialOrder<T>
{
    static abstract Ordering CompareOrder(T left, T right);
}
