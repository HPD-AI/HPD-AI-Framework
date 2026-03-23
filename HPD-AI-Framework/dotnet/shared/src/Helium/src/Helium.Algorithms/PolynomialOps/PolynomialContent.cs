using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algorithms;

/// <summary>
/// Content and primitive part of a polynomial over a GCD domain.
/// Content = GCD of all coefficients. Primitive part = polynomial / content.
/// </summary>
public static class PolynomialContent
{
    /// <summary>
    /// Computes the content of a polynomial: the GCD of all its coefficients.
    /// Returns zero for the zero polynomial.
    /// </summary>
    public static R Compute<R>(Polynomial<R> p)
        where R : IRing<R>, IGcdDomain<R>
    {
        if (p.IsZero)
            return R.AdditiveIdentity;

        var result = R.AdditiveIdentity;
        foreach (var exp in p.Support)
        {
            result = R.Gcd(result, p[exp]);
        }
        return result;
    }

    /// <summary>
    /// Computes the primitive part of a polynomial: p / content(p).
    /// Each coefficient is divided by the content using DivMod.
    /// Returns zero for the zero polynomial.
    /// </summary>
    public static Polynomial<R> PrimitivePart<R>(Polynomial<R> p)
        where R : IRing<R>, IGcdDomain<R>, IEuclideanDomain<R>
    {
        if (p.IsZero)
            return Polynomial<R>.Zero;

        var c = Compute(p);
        if (c.Equals(R.MultiplicativeIdentity))
            return p;

        // Divide each coefficient by the content.
        var coeffs = new List<KeyValuePair<int, R>>();
        foreach (var exp in p.Support)
        {
            var (q, _) = R.DivMod(p[exp], c);
            coeffs.Add(new(exp, q));
        }
        return Polynomial<R>.FromCoeffs(CoeffsToArray(coeffs));
    }

    private static R[] CoeffsToArray<R>(List<KeyValuePair<int, R>> coeffs)
        where R : IRing<R>
    {
        if (coeffs.Count == 0)
            return [];

        int maxDeg = 0;
        foreach (var kv in coeffs)
            if (kv.Key > maxDeg) maxDeg = kv.Key;

        var result = new R[maxDeg + 1];
        Array.Fill(result, R.AdditiveIdentity);
        foreach (var kv in coeffs)
            result[kv.Key] = kv.Value;
        return result;
    }
}
