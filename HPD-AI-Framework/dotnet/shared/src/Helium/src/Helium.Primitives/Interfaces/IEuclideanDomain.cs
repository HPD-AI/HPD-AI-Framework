namespace Helium.Primitives;

/// <summary>
/// A Euclidean domain: a GCD domain with a division algorithm.
/// For nonzero b: a == quotient * b + remainder, with Size(remainder) &lt; Size(b).
/// </summary>
public interface IEuclideanDomain<T> : IGcdDomain<T>
    where T : IEuclideanDomain<T>, IRing<T>
{
    static abstract (T Quotient, T Remainder) DivMod(T a, T b);
}
