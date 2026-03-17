using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algorithms;

/// <summary>
/// Groebner basis computation via Buchberger's algorithm for multivariate polynomials over a field.
/// </summary>
public static class GroebnerBasis
{
    /// <summary>
    /// Multivariate polynomial division: divides f by the list of divisors.
    /// Returns the remainder after division by all divisors.
    /// </summary>
    public static MvPolynomial<R> Reduce<R>(MvPolynomial<R> f, IReadOnlyList<MvPolynomial<R>> divisors)
        where R : IField<R>, ICommRing<R>
    {
        var remainder = MvPolynomial<R>.Zero;
        var p = f;

        while (!p.IsZero)
        {
            bool divided = false;
            var lm = p.LeadingMonomial;
            var lc = p.LeadingCoefficient;

            for (int i = 0; i < divisors.Count; i++)
            {
                var gi = divisors[i];
                if (gi.IsZero) continue;

                var lmg = gi.LeadingMonomial;
                if (lmg.Divides(lm))
                {
                    var quotientMonomial = lm - lmg;
                    var quotientCoeff = lc * R.Invert(gi.LeadingCoefficient);
                    var term = MvPolynomial<R>.Term(quotientMonomial, quotientCoeff);
                    p = p - term * gi;
                    divided = true;
                    break;
                }
            }

            if (!divided)
            {
                // Move leading term to remainder.
                remainder = remainder + MvPolynomial<R>.Term(lm, lc);
                p = p - MvPolynomial<R>.Term(lm, lc);
            }
        }

        return remainder;
    }

    /// <summary>
    /// Computes the S-polynomial of f and g.
    /// S(f, g) = (lcm(LM(f), LM(g)) / LT(f)) * f - (lcm(LM(f), LM(g)) / LT(g)) * g
    /// </summary>
    public static MvPolynomial<R> SPolynomial<R>(MvPolynomial<R> f, MvPolynomial<R> g)
        where R : IField<R>, ICommRing<R>
    {
        if (f.IsZero || g.IsZero)
            return MvPolynomial<R>.Zero;

        var lmf = f.LeadingMonomial;
        var lmg = g.LeadingMonomial;
        var lcmMon = MonomialLcm(lmf, lmg);

        var lcf = f.LeadingCoefficient;
        var lcg = g.LeadingCoefficient;

        var termF = MvPolynomial<R>.Term(lcmMon - lmf, R.Invert(lcf));
        var termG = MvPolynomial<R>.Term(lcmMon - lmg, R.Invert(lcg));

        return termF * f - termG * g;
    }

    /// <summary>
    /// Computes a Groebner basis from a set of generators using Buchberger's algorithm.
    /// The result generates the same ideal as the input.
    /// </summary>
    public static List<MvPolynomial<R>> Compute<R>(IReadOnlyList<MvPolynomial<R>> generators)
        where R : IField<R>, ICommRing<R>
    {
        var basis = new List<MvPolynomial<R>>();
        foreach (var g in generators)
        {
            if (!g.IsZero)
                basis.Add(g);
        }

        if (basis.Count == 0)
            return basis;

        // Track pairs we need to check.
        var pairs = new List<(int, int)>();
        for (int i = 0; i < basis.Count; i++)
            for (int j = i + 1; j < basis.Count; j++)
                pairs.Add((i, j));

        while (pairs.Count > 0)
        {
            var (i, j) = pairs[0];
            pairs.RemoveAt(0);

            var spoly = SPolynomial(basis[i], basis[j]);
            var remainder = Reduce(spoly, basis);

            if (!remainder.IsZero)
            {
                // Make monic.
                var lc = remainder.LeadingCoefficient;
                remainder = MvPolynomial<R>.C(R.Invert(lc)) * remainder;

                int newIdx = basis.Count;
                for (int k = 0; k < basis.Count; k++)
                    pairs.Add((k, newIdx));
                basis.Add(remainder);
            }
        }

        return basis;
    }

    /// <summary>
    /// Computes the reduced Groebner basis: each element is monic, and no monomial
    /// of any element is divisible by the leading monomial of another element.
    /// The reduced Groebner basis is unique for a given ideal and monomial order.
    /// </summary>
    public static List<MvPolynomial<R>> ComputeReduced<R>(IReadOnlyList<MvPolynomial<R>> generators)
        where R : IField<R>, ICommRing<R>
    {
        var basis = Compute(generators);

        // Reduce: remove elements whose leading monomial is divisible by another's.
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < basis.Count; i++)
            {
                var others = new List<MvPolynomial<R>>();
                for (int j = 0; j < basis.Count; j++)
                    if (j != i) others.Add(basis[j]);

                var reduced = Reduce(basis[i], others);
                if (reduced.IsZero)
                {
                    basis.RemoveAt(i);
                    changed = true;
                    break;
                }

                if (!reduced.Equals(basis[i]))
                {
                    // Make monic.
                    var lc = reduced.LeadingCoefficient;
                    reduced = MvPolynomial<R>.C(R.Invert(lc)) * reduced;
                    basis[i] = reduced;
                    changed = true;
                }
            }
        }

        // Sort by leading monomial.
        basis.Sort((a, b) => a.LeadingMonomial.CompareTo(b.LeadingMonomial));

        return basis;
    }

    /// <summary>
    /// Checks if a polynomial is in the ideal generated by the basis
    /// by reducing it modulo the basis.
    /// </summary>
    public static bool IsInIdeal<R>(MvPolynomial<R> f, IReadOnlyList<MvPolynomial<R>> groebnerBasis)
        where R : IField<R>, ICommRing<R>
    {
        return Reduce(f, groebnerBasis).IsZero;
    }

    /// <summary>
    /// Computes the LCM of two monomials (componentwise max of exponents).
    /// </summary>
    private static Monomial MonomialLcm(Monomial a, Monomial b)
    {
        var allVars = a.Variables.Concat(b.Variables).Distinct();
        var exponents = new List<KeyValuePair<int, Integer>>();
        foreach (var v in allVars)
        {
            var ea = a[v];
            var eb = b[v];
            var max = ea > eb ? ea : eb;
            exponents.Add(new(v, max));
        }
        return Monomial.FromExponents(exponents);
    }
}
