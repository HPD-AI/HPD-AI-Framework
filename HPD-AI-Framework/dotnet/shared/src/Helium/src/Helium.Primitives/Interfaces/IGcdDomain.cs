namespace Helium.Primitives;

/// <summary>
/// A GCD domain: an integral domain where any two elements have a greatest common divisor.
/// </summary>
public interface IGcdDomain<T>
    where T : IGcdDomain<T>, IRing<T>
{
    static abstract T Gcd(T a, T b);
    static abstract T Lcm(T a, T b);
}
