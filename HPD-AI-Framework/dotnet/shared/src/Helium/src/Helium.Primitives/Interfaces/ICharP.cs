namespace Helium.Primitives;

/// <summary>
/// Characteristic of a ring: smallest positive p such that p · 1 = 0, or 0 if none exists.
/// </summary>
public interface ICharP<T>
    where T : ICharP<T>
{
    static abstract int Characteristic { get; }
}
