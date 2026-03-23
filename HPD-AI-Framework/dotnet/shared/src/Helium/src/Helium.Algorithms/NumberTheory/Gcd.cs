using Helium.Primitives;

namespace Helium.Algorithms;

/// <summary>
/// Generic Euclidean and extended Euclidean algorithms over any IEuclideanDomain.
/// </summary>
public static class Gcd
{
    /// <summary>
    /// Computes gcd(a, b) via the Euclidean algorithm.
    /// </summary>
    public static T Compute<T>(T a, T b)
        where T : IEuclideanDomain<T>, IRing<T>
    {
        while (!b.Equals(T.AdditiveIdentity))
        {
            var (_, r) = T.DivMod(a, b);
            a = b;
            b = r;
        }
        return a;
    }

    /// <summary>
    /// Extended Euclidean algorithm: returns (gcd, s, t) such that a*s + b*t == gcd.
    /// </summary>
    public static (T Gcd, T S, T T) Extended<T>(T a, T b)
        where T : IEuclideanDomain<T>, IRing<T>
    {
        var oldR = a;
        var r = b;
        var oldS = T.MultiplicativeIdentity;
        var s = T.AdditiveIdentity;
        var oldT = T.AdditiveIdentity;
        var t = T.MultiplicativeIdentity;

        while (!r.Equals(T.AdditiveIdentity))
        {
            var (q, rem) = T.DivMod(oldR, r);
            (oldR, r) = (r, rem);
            (oldS, s) = (s, oldS - q * s);
            (oldT, t) = (t, oldT - q * t);
        }

        return (oldR, oldS, oldT);
    }
}

/// <summary>
/// Generic LCM via GCD.
/// </summary>
public static class Lcm
{
    /// <summary>
    /// Computes lcm(a, b) = a * b / gcd(a, b). Returns zero if either input is zero.
    /// </summary>
    public static T Compute<T>(T a, T b)
        where T : IEuclideanDomain<T>, IRing<T>
    {
        if (a.Equals(T.AdditiveIdentity) || b.Equals(T.AdditiveIdentity))
            return T.AdditiveIdentity;

        var g = Gcd.Compute(a, b);
        var (q, _) = T.DivMod(a, g);
        return q * b;
    }
}
