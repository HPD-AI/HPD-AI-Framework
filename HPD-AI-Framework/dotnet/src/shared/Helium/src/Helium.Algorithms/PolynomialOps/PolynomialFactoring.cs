using System.Numerics;
using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algorithms;

/// <summary>
/// Polynomial factoring over Z and Q.
/// Current implementation provides square-free decomposition and integer/rational-root extraction.
/// </summary>
public static class PolynomialFactoring
{
    public readonly record struct FactorOverZDiagnostics(
        int PrimeAttempts,
        int PrimeAccepted,
        int SubsetMasksTried,
        int HenselLiftAttempts,
        int HenselLiftSuccesses,
        int LllDimension);

    private sealed class DiagnosticsAccumulator
    {
        public int PrimeAttempts;
        public int PrimeAccepted;
        public int SubsetMasksTried;
        public int HenselLiftAttempts;
        public int HenselLiftSuccesses;
        public int LllDimension;

        public FactorOverZDiagnostics Snapshot() =>
            new(PrimeAttempts, PrimeAccepted, SubsetMasksTried, HenselLiftAttempts, HenselLiftSuccesses, LllDimension);
    }

    public static (Rational Content, List<(Polynomial<Rational> Factor, int Multiplicity)> Factors)
        Factor(Polynomial<Rational> f)
    {
        if (f.IsZero)
            return (Rational.Zero, []);

        if (f.Degree <= 0)
            return (f[0], []);

        var integerPoly = ClearDenominators(f, out var denominatorLcm);
        var content = PolynomialContent.Compute(integerPoly);
        var primitive = PolynomialContent.PrimitivePart(integerPoly);
        var overZ = FactorOverZ(primitive);

        var rationalContent = Rational.Create(content, denominatorLcm);
        var factors = overZ
            .Select(x => (ToRational(x.Factor), x.Multiplicity))
            .ToList();

        return (rationalContent, factors);
    }

    public static List<(Polynomial<Integer> Factor, int Multiplicity)>
        FactorOverZ(Polynomial<Integer> f)
    {
        return FactorOverZWithDiagnostics(f).Factors;
    }

    public static (List<(Polynomial<Integer> Factor, int Multiplicity)> Factors, FactorOverZDiagnostics Diagnostics)
        FactorOverZWithDiagnostics(Polynomial<Integer> f)
    {
        var diagnostics = new DiagnosticsAccumulator();
        if (f.IsZero || f.Degree <= 0)
            return ([], diagnostics.Snapshot());

        var primitive = NormalizePrimitive(PolynomialContent.PrimitivePart(f));
        if (primitive.Degree <= 0)
            return ([], diagnostics.Snapshot());

        var result = new List<(Polynomial<Integer> Factor, int Multiplicity)>();
        var squareFree = SquareFreeFactorization(primitive);
        foreach (var (part, multiplicity) in squareFree)
        {
            var split = FactorSquareFreeOverZ(part, diagnostics);
            foreach (var factor in split)
                result.Add((factor, multiplicity));
        }

        return (MergeMultiplicities(result), diagnostics.Snapshot());
    }

    public static List<(Polynomial<Integer> Factor, int Multiplicity)>
        SquareFreeFactorization(Polynomial<Integer> f)
    {
        if (f.IsZero || f.Degree <= 0)
            return [];

        var primitive = NormalizePrimitive(PolynomialContent.PrimitivePart(f));
        var fr = ToRational(primitive);
        var derivative = PolynomialCalculus.Derivative(fr);

        var g = fr.Gcd(derivative);
        var w = DivideExact(fr, g);
        var i = 1;
        var output = new List<(Polynomial<Integer> Factor, int Multiplicity)>();

        while (!IsOne(w))
        {
            var y = w.Gcd(g);
            var z = DivideExact(w, y);
            if (!IsOne(z))
                output.Add((NormalizePrimitive(ToIntegerPrimitive(z)), i));

            w = y;
            g = DivideExact(g, y);
            i++;
        }

        return output;
    }

    public static List<Polynomial<QuotientRing<Integer>>>
        FactorOverFiniteField(Polynomial<QuotientRing<Integer>> f, int p)
    {
        if (p <= 1)
            throw new ArgumentOutOfRangeException(nameof(p), "Modulus must be greater than 1.");

        if (f.IsZero || f.Degree <= 0)
            return [];

        var input = Monic(ToModCoefficients(f, p), p);
        var squareFreeParts = SquareFreeFactorizationMod(input, p);
        var output = new List<Polynomial<QuotientRing<Integer>>>();

        foreach (var (part, multiplicity) in squareFreeParts)
        {
            var partPoly = FromModCoefficients(part, p);
            var factors = p <= 7
                ? Berlekamp(partPoly, p)
                : CantorZassenhaus(partPoly, p);

            if (factors.Count == 0)
                factors = [partPoly];

            for (int i = 0; i < multiplicity; i++)
                output.AddRange(factors);
        }

        return output;
    }

    public static List<Polynomial<QuotientRing<Integer>>>
        Berlekamp(Polynomial<QuotientRing<Integer>> f, int p)
    {
        if (p <= 1)
            throw new ArgumentOutOfRangeException(nameof(p), "Modulus must be greater than 1.");

        if (f.IsZero || f.Degree <= 0)
            return [];

        var input = ToModCoefficients(f, p);
        var work = new List<int[]> { input };
        var output = new List<int[]>();

        while (work.Count > 0)
        {
            var h = work[^1];
            work.RemoveAt(work.Count - 1);

            if (Degree(h) <= 1)
            {
                output.Add(h);
                continue;
            }

            var split = BerlekampSplitOnce(h, p);
            if (split is null)
            {
                output.Add(h);
                continue;
            }

            work.Add(split.Value.Left);
            work.Add(split.Value.Right);
        }

        return output.Select(x => FromModCoefficients(x, p)).ToList();
    }

    public static List<Polynomial<QuotientRing<Integer>>>
        CantorZassenhaus(Polynomial<QuotientRing<Integer>> f, int p)
    {
        if (p <= 1)
            throw new ArgumentOutOfRangeException(nameof(p), "Modulus must be greater than 1.");

        if (f.IsZero || f.Degree <= 0)
            return [];

        var input = Monic(ToModCoefficients(f, p), p);
        var output = new List<int[]>();
        var distinctDegree = DistinctDegreeFactorization(input, p);
        foreach (var (part, d) in distinctDegree)
        {
            var pieces = EqualDegreeFactorization(part, d, p);
            output.AddRange(pieces);
        }

        return output.Select(x => FromModCoefficients(x, p)).ToList();
    }

    public static (Polynomial<Integer> G, Polynomial<Integer> H)
        HenselLift(Polynomial<Integer> f,
                   Polynomial<Integer> g, Polynomial<Integer> h,
                   Integer p, int precision)
    {
        if (precision < 1)
            throw new ArgumentOutOfRangeException(nameof(precision), "Precision must be at least 1.");

        var pBig = (BigInteger)p;
        if (pBig <= 1 || pBig > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(p), "Prime modulus must be in [2, int.MaxValue].");

        int pInt = (int)pBig;
        if (f.IsZero || g.IsZero || h.IsZero)
            return (g, h);

        var gk = g;
        var hk = h;

        var gBar = ToModCoefficients(gk, pInt);
        var hBar = ToModCoefficients(hk, pInt);
        var (gcd, s, t) = ExtendedGcdMod(gBar, hBar, pInt);
        if (Degree(gcd) != 0)
            throw new InvalidOperationException("Hensel lift requires gcd(g, h) = 1 mod p.");

        var modulusPow = p;
        int currentExponent = 1;
        while (currentExponent < precision)
        {
            // e = (f - g_k*h_k) / p^k  (coefficient-wise exact division)
            var diff = f - gk * hk;
            var c = DivideByScalarPower(diff, modulusPow);
            var e = ToModCoefficients(c, pInt);

            // Solve: g*b + h*a ≡ e (mod p) using s*g + t*h ≡ 1 (mod p).
            // Compute s*e, then reduce mod h to keep degrees bounded:
            //   (q, a) = divmod(s*e, h) mod p
            //   b = t*e + q*g mod p
            var se = MulMod(s, e, pInt);
            var (q, aMod) = DivRemMod(se, hBar, pInt);
            var bMod = AddMod(MulMod(t, e, pInt), MulMod(q, gBar, pInt), pInt);

            var aInt = FromSmallModCoefficients(aMod);
            var bInt = FromSmallModCoefficients(bMod);

            gk = gk + ScaleByInteger(bInt, modulusPow);
            hk = hk + ScaleByInteger(aInt, modulusPow);

            modulusPow = modulusPow * p;
            currentExponent++;
        }

        return (gk, hk);
    }

    private static List<Polynomial<Integer>> FactorSquareFreeOverZ(Polynomial<Integer> f, DiagnosticsAccumulator diagnostics)
    {
        var factors = new List<Polynomial<Integer>>();
        var current = NormalizePrimitive(f);

        while (current.Degree > 0)
        {
            if (current[0].IsZero)
            {
                factors.Add(Polynomial<Integer>.X);
                current = DivideByLinearExact(current, Integer.Zero);
                continue;
            }

            var roots = IntegerRootCandidates(current).ToList();
            var found = false;
            foreach (var root in roots)
            {
                if (!TryDivideByLinear(current, root, out var quotient))
                    continue;

                factors.Add(Polynomial<Integer>.FromCoeffs(-root, Integer.One));
                current = NormalizePrimitive(quotient);
                found = true;
                break;
            }

            if (!found)
            {
                if (TrySplitWithTwoFactorHensel(current, diagnostics, out var left, out var right))
                {
                    factors.AddRange(FactorSquareFreeOverZ(left, diagnostics));
                    factors.AddRange(FactorSquareFreeOverZ(right, diagnostics));
                }
                else
                {
                    factors.Add(NormalizePrimitive(current));
                }
                break;
            }
        }

        return factors;
    }

    // -------------------------------------------------------------------------
    // Van Hoeij recombination (replaces subset recombination)
    // -------------------------------------------------------------------------

    // Entry point: tries van Hoeij factoring. Returns list of all irreducible factors
    // (possibly just [f] if f is irreducible), or null if no good prime was found.
    private static List<Polynomial<Integer>>? TrySplitWithVanHoeij(
        Polynomial<Integer> f,
        DiagnosticsAccumulator diagnostics)
    {
        if (f.Degree <= 1)
            return null;

        int maxPrimes = ComputePrimeBudget(f.Degree);
        foreach (int p in EnumerateGoodRecombinationPrimes(f, diagnostics, maxPrimes))
        {
            var rawModPoly = ToModCoefficients(f, p);
            var modPoly    = Monic(rawModPoly, p);
            if (Degree(modPoly) <= 1) continue;

            var ffInput   = FromModCoefficients(modPoly, p);
            var ffFactors = p <= 7 ? Berlekamp(ffInput, p) : CantorZassenhaus(ffInput, p);
            if (ffFactors.Count < 2) continue;

            var ffPieces = ffFactors
                .Select(x => Monic(ToModCoefficients(x, p), p))
                .Where(x => Degree(x) > 0)
                .ToList();
            if (ffPieces.Count < 2) continue;

            // Compute Hensel lift precision.
            var bound      = MignotteBound(f);
            var target     = bound * (Integer)2;
            var modulusPow = (Integer)p;
            int precision  = 1;
            while ((BigInteger)modulusPow <= (BigInteger)target)
            {
                modulusPow *= (Integer)p;
                precision++;
            }

            diagnostics.HenselLiftAttempts++;

            // Multi-factor Hensel lift: lift all ffPieces simultaneously by successive pairwise lifting.
            // Simple approach: lift the full product tree.
            var liftedFactors = HenselLiftAll(f, ffPieces, p, precision, modulusPow);
            if (liftedFactors is null) continue;

            diagnostics.HenselLiftSuccesses++;

            var result = VanHoeijRecombine(f, liftedFactors, modulusPow, diagnostics);
            return result;
        }

        return null;
    }

    // Multi-factor Hensel lift: lift all l modular factors to precision p^k.
    // Strategy: lift each factor g_i against h_i = f / g_i (mod p) using binary Hensel.
    // Returns centered-lifted Integer polynomials mod p^k, one per modular factor.
    private static Polynomial<Integer>[]? HenselLiftAll(
        Polynomial<Integer> f,
        List<int[]> modFactors,
        int p,
        int precision,
        Integer modulusPow)
    {
        int l = modFactors.Count;
        var lifted = new Polynomial<Integer>[l];
        int leadingMod = NormalizeBigMod((BigInteger)f.LeadingCoefficient, p);

        try
        {
            for (int idx = 0; idx < l; idx++)
            {
                // g0 = modFactors[idx]; h0 = product of all others.
                var g0 = Monic(modFactors[idx], p);
                var h0 = new[] { 1 };
                for (int i = 0; i < l; i++)
                    if (i != idx) h0 = MulMod(h0, modFactors[i], p);
                h0 = Monic(h0, p);

                // Scale g0 so lc(g0) * lc(h0) ≡ lc(f) (mod p).
                g0 = ScaleMod(g0, leadingMod, p);

                var (gLift, _) = HenselLift(f, FromSmallModCoefficients(g0), FromSmallModCoefficients(h0), (Integer)p, precision);
                lifted[idx] = CenterCoefficientsMod(gLift, modulusPow);
            }

            return lifted;
        }
        catch
        {
            return null;
        }
    }

    // Van Hoeij recombination via incremental Newton-sum LLL (PARI LLL_cmbf algorithm).
    //
    // Instead of building a static coefficient matrix, we track combination vectors CM_L
    // (initially C * I_l) and add one Newton power sum per round, LLL-reducing after each.
    // A true factor subset S has Newton sums bounded by the Newton-sum bound B, so the
    // combination vector for S stays short. LLL finds it; we then multiply the selected
    // g_i together and trial-divide to confirm.
    //
    // Reference: PARI QX_factor.c — LLL_cmbf, chk_factors, combine_factors.
    private static List<Polynomial<Integer>> VanHoeijRecombine(
        Polynomial<Integer> f,
        Polynomial<Integer>[] liftedFactors,
        Integer modulus,
        DiagnosticsAccumulator diagnostics)
    {
        int l = liftedFactors.Length;   // number of lifted modular factors
        int n = f.Degree;

        diagnostics.LllDimension = l + 1; // grows by 1 per Newton-sum round

        // C = scaling constant (avoids fractions in LLL matrix).
        // PARI uses C = ceil(sqrt(N0 * l / 4)) with N0=1.
        int C = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(l / 4.0)));

        // CM_L: l × l matrix, initially C * I_l.
        // Each column is a combination vector (encodes a candidate subset of g_i's).
        // After LLL, columns with small norm identify true factor subsets.
        var cmL = new Integer[l * l];
        for (int i = 0; i < l; i++)
            cmL[i * l + i] = (Integer)C;

        // Newton-sum cache: newtonSums[i][k] = p_k(g_i) mod modulus (1-indexed k).
        // We compute one new sum per round via the Newton identity recurrence.
        var newtonCache = new List<Integer>[l];
        for (int i = 0; i < l; i++)
            newtonCache[i] = new List<Integer> { Integer.Zero }; // index 0 unused

        var remaining  = NormalizePrimitive(f);
        var result     = new List<Polynomial<Integer>>();
        var lc         = f.LeadingCoefficient;
        bool lcIsOne   = lc == Integer.One || lc == -Integer.One;

        // Newton-sum bound B: n * lc² * root_bound²  (simplified: use modulus/4 as threshold).
        // Bnorm threshold: l * (C² + l/4).
        double bnorm = l * ((double)C * C + l / 4.0);

        // Precomputed liftedFactors as int[] coefficient arrays mod modulus for Newton recurrence.
        // Newton recurrence (polsym_gen): p_k = - Σ_{j=1}^{deg} a_{deg-j} * p_{k-j} / a_{deg}
        // where g = a_deg * x^deg + ... + a_0, p_k = Σ roots^k.
        // Equivalently for monic g: p_k + a_1 p_{k-1} + ... + a_{k-1} p_1 + k*a_k = 0  (k ≤ deg)
        //                           p_k + a_1 p_{k-1} + ... + a_deg p_{k-deg}     = 0  (k > deg)

        int maxRounds = 2 * n + 2; // generous upper bound; PARI adapts dynamically

        for (int tmax = 0; tmax < maxRounds && remaining.Degree > 0; tmax++)
        {
            int tNew = tmax + 1; // 1-indexed Newton sum to add this round

            // ---- 1. Compute Newton sum p_{tNew}(g_i) for each i ----
            for (int i = 0; i < l; i++)
            {
                var gi    = liftedFactors[i];
                var cache = newtonCache[i];
                var pk    = NewtonSumMod(gi, tNew, cache, modulus);
                // Make integral when f has non-unit leading coefficient.
                if (!lcIsOne)
                {
                    var lcPow = BigIntPow((BigInteger)lc.Abs(), tNew);
                    pk = (Integer)(((BigInteger)pk * lcPow) % (BigInteger)modulus);
                }
                cache.Add(pk);
            }

            // ---- 2. Compute r columns of M_L = CM_L / C ----
            int r = l; // current rank (all columns of CM_L used)

            // Tra[i] = p_{tNew}(g_i) (one Newton sum per round, N0=1).
            var tra = new Integer[l];
            for (int i = 0; i < l; i++)
                tra[i] = newtonCache[i][tNew];

            // T2[j] = Σ_i Tra[i] * (CM_L[i, j] / C)  mod modulus, then truncate by p^{a-b}.
            // For simplicity we use b = a (full precision) — PARI truncates to save LLL work,
            // but correctness is preserved without truncation.
            var t2 = new Integer[r];
            for (int j = 0; j < r; j++)
            {
                BigInteger s = BigInteger.Zero;
                for (int i = 0; i < l; i++)
                    s += (BigInteger)tra[i] * (BigInteger)cmL[i * l + j];
                // Divide by C and center mod modulus.
                s = s / C;
                var mod = (BigInteger)modulus;
                s = ((s % mod) + mod) % mod;
                if (s > mod / 2) s -= mod;
                t2[j] = (Integer)s;
            }

            // ---- 3. Build the (l+1) × (l+1) matrix m for LLL ----
            //
            //     [ C · CM_L / C  (= CM_L)   |    0    ]   ← l rows
            // m = [─────────────────────────────────────]
            //     [       T2                  |    1    ]   ← 1 row (one Newton sum)
            //
            // Here we use p^{a-b} = 1 (no truncation) for the scale of the Newton-sum row.
            int dim = l + 1;
            var mEntries = new Integer[dim * dim];

            // Top l rows: CM_L block on the left, 0 on the right.
            for (int i = 0; i < l; i++)
                for (int j = 0; j < l; j++)
                    mEntries[i * dim + j] = cmL[i * l + j];
            // mEntries[i * dim + l] = 0 (already zero-init)

            // Bottom row: T2 on the left, 1 on the right.
            for (int j = 0; j < r; j++)
                mEntries[l * dim + j] = t2[j];
            mEntries[l * dim + l] = Integer.One;

            var m = Matrix<Integer>.FromArray(dim, dim, mEntries);

            // ---- 4. LLL-reduce m ----
            var mRed = LatticeReduction.LLL(m);

            // ---- 5. Extract updated CM_L: rows of mRed whose left-l-col norm < Bnorm ----
            // These are the short rows — their left l columns are the updated combination vectors.
            var newCmLRows = new List<Integer[]>();
            for (int row = 0; row < dim; row++)
            {
                double normSq = 0.0;
                for (int j = 0; j < l; j++)
                {
                    double v = (double)(BigInteger)mRed[row, j];
                    normSq += v * v;
                }
                if (normSq < bnorm * 1.00001)
                {
                    var rowData = new Integer[l];
                    for (int j = 0; j < l; j++)
                        rowData[j] = mRed[row, j];
                    newCmLRows.Add(rowData);
                }
            }

            if (newCmLRows.Count == 0 || newCmLRows.Count >= l)
            {
                // No rank decrease: no factors found this round. Continue adding Newton sums.
                // Rebuild cmL from mRed's first l rows (LLL updated them).
                for (int i = 0; i < l; i++)
                    for (int j = 0; j < l; j++)
                        cmL[i * l + j] = mRed[i, j];
                continue;
            }

            // Rank decreased: try to extract factors from the combination vectors.
            // CM_L columns are combination vectors scaled by C; divide by C to get 0/1 masks.
            // Each column of CM_L / C whose entries are 0 or ±1 identifies a subset S.
            var candidates = ExtractFactorCandidates(newCmLRows, l, C, liftedFactors, modulus);

            bool anyFound = false;
            foreach (var candidate in candidates)
            {
                if (candidate.IsZero || candidate.Degree <= 0 || candidate.Degree >= remaining.Degree)
                    continue;
                var norm = NormalizePrimitive(candidate);
                if (TryDivideExactOverZ(remaining, norm, out var quotient))
                {
                    result.Add(norm);
                    remaining = NormalizePrimitive(quotient);
                    anyFound  = true;
                    if (remaining.Degree == 0) break;
                }
            }

            // Rebuild cmL from the short rows for the next round.
            int newL = newCmLRows.Count;
            var newCmL = new Integer[newL * newL];
            for (int i = 0; i < newL; i++)
                for (int j = 0; j < Math.Min(newL, newCmLRows[i].Length); j++)
                    newCmL[i * newL + j] = newCmLRows[i][j < newCmLRows[i].Length ? j : 0];

            if (anyFound && newL < l)
            {
                // Shrink state to the remaining factors.
                // Simple: restart with reduced f, keeping same lifted factors minus the ones used.
                // For correctness we fall through to the end and let remaining carry the rest.
                break;
            }

            // Update cmL for next round.
            for (int i = 0; i < l; i++)
                for (int j = 0; j < l; j++)
                    cmL[i * l + j] = i < newL && j < newL ? newCmL[i * newL + j] : Integer.Zero;
        }

        // Whatever remains is irreducible (or 1).
        if (remaining.Degree > 0)
            result.Add(NormalizePrimitive(remaining));

        if (result.Count == 0)
            result.Add(NormalizePrimitive(f));

        return result;
    }

    // Compute the k-th Newton power sum p_k(g) = Σ roots^k of g, mod modulus.
    // Uses the Newton identity recurrence (polsym_gen):
    //   For monic g = x^d + a_{d-1} x^{d-1} + ... + a_0:
    //     p_k + a_{d-1} p_{k-1} + ... + a_{d-k+1} p_1 + k * a_{d-k} = 0  for k ≤ d
    //     p_k + a_{d-1} p_{k-1} + ... + a_0 p_{k-d}                  = 0  for k > d
    // cache[j] = p_j (1-indexed), cache[0] = 0 (unused placeholder).
    private static Integer NewtonSumMod(
        Polynomial<Integer> g, int k, List<Integer> cache, Integer modulus)
    {
        // Return cached value if already computed.
        if (k < cache.Count) return cache[k];

        int d = g.Degree;
        if (d <= 0) return Integer.Zero;

        var mod = (BigInteger)modulus;

        // Monic coefficients: g = x^d + c[d-1] x^{d-1} + ... + c[0].
        // If g is not monic, we compute for lc(g)^k * p_k(g/lc(g)) = power sum of scaled roots.
        // For the van Hoeij application, g is stored centered-lifted; treat as-is.
        // Newton recurrence coefficients: a[j] = g[d-j] / g[d] (coefficient of x^{d-j}).
        // We work mod modulus throughout.

        BigInteger pk;
        if (k <= d)
        {
            // p_k = - Σ_{j=1}^{k-1} a[j] * p_{k-j}  -  k * a[k]
            // where a[j] = g[d-j] (for monic g, g[d] = 1).
            BigInteger sum = BigInteger.Zero;
            for (int j = 1; j < k; j++)
            {
                var aj = (BigInteger)(j <= d ? g[d - j] : Integer.Zero);
                var pj = (BigInteger)NewtonSumMod(g, k - j, cache, modulus);
                sum = (sum + aj * pj) % mod;
            }
            var ak = (BigInteger)(k <= d ? g[d - k] : Integer.Zero);
            pk = ((-sum - (BigInteger)k * ak) % mod + mod) % mod;
        }
        else
        {
            // p_k = - Σ_{j=1}^{d} a[j] * p_{k-j}
            BigInteger sum = BigInteger.Zero;
            for (int j = 1; j <= d; j++)
            {
                var aj = (BigInteger)g[d - j];
                var pj = (BigInteger)NewtonSumMod(g, k - j, cache, modulus);
                sum = (sum + aj * pj) % mod;
            }
            pk = ((-sum) % mod + mod) % mod;
        }

        // Center in (-modulus/2, modulus/2].
        if (pk > mod / 2) pk -= mod;
        return (Integer)pk;
    }

    private static BigInteger BigIntPow(BigInteger b, int exp)
    {
        BigInteger result = BigInteger.One;
        for (int i = 0; i < exp; i++)
            result *= b;
        return result;
    }

    // Given the short LLL rows (each of length l, scaled by C), extract factor candidates.
    // Each row encodes a combination: divide entries by C to get ±1 or 0 subset indicators.
    // Collect the g_i where the indicator is nonzero, multiply them mod modulus, center-lift.
    private static List<Polynomial<Integer>> ExtractFactorCandidates(
        List<Integer[]> cmLRows,
        int l,
        int C,
        Polynomial<Integer>[] liftedFactors,
        Integer modulus)
    {
        var candidates = new List<Polynomial<Integer>>();
        var ci = (Integer)C;

        // Transpose: cmLRows[row][col] — we want to look at each column as a combination vector.
        int r = cmLRows.Count;
        for (int col = 0; col < r; col++)
        {
            // Combination vector: v[i] = cmLRows[row][col] / C for each row.
            // If all entries are 0 or ±C, this is a 0/1 mask.
            bool valid = true;
            var mask = new int[l];
            for (int i = 0; i < l; i++)
            {
                var entry = (BigInteger)cmLRows[i < r ? i : 0][col < cmLRows[0].Length ? col : 0];
                var rem = entry % C;
                if (rem != 0) { valid = false; break; }
                var v = (int)(entry / C);
                if (v != 0 && v != 1 && v != -1) { valid = false; break; }
                mask[i] = v;
            }
            if (!valid) continue;

            // Multiply the selected g_i together mod modulus.
            bool started = false;
            Polynomial<Integer> product = Polynomial<Integer>.One;
            for (int i = 0; i < l; i++)
            {
                if (mask[i] == 0) continue;
                product = started
                    ? MultiplyModPoly(product, liftedFactors[i], modulus)
                    : liftedFactors[i];
                started = true;
            }
            if (!started) continue;

            candidates.Add(CenterCoefficientsMod(product, modulus));
        }

        return candidates;
    }

    // Multiply two polynomials with coefficients mod modulus.
    private static Polynomial<Integer> MultiplyModPoly(
        Polynomial<Integer> a, Polynomial<Integer> b, Integer modulus)
    {
        int da = a.Degree, db = b.Degree;
        var coeffs = new Integer[da + db + 1];
        var mod    = (BigInteger)modulus;
        for (int i = 0; i <= da; i++)
            for (int j = 0; j <= db; j++)
            {
                var prod = ((BigInteger)a[i] * (BigInteger)b[j]) % mod;
                coeffs[i + j] = (Integer)(((BigInteger)coeffs[i + j] + prod) % mod);
            }
        return Polynomial<Integer>.FromCoeffs(coeffs);
    }

    // Mignotte bound: C(n, n/2) * ||f||_2 * max(|lc|, |f(0)|)
    // Returns a valid upper bound B such that any factor coefficient has absolute value ≤ B.
    private static Integer MignotteBound(Polynomial<Integer> f)
    {
        if (f.IsZero) return Integer.One;

        int n = f.Degree;

        BigInteger l1 = BigInteger.Zero;
        for (int i = 0; i <= n; i++)
            l1 += BigInteger.Abs((BigInteger)f[i]);

        var binom = Binomial(n, n / 2);
        var bound = binom * l1;
        return bound <= BigInteger.Zero ? Integer.One : (Integer)bound;
    }

    // -------------------------------------------------------------------------
    // Two-factor Hensel split (kept for fallback / small cases)
    // -------------------------------------------------------------------------

    private static bool TrySplitWithTwoFactorHensel(
        Polynomial<Integer> f,
        DiagnosticsAccumulator diagnostics,
        out Polynomial<Integer> left,
        out Polynomial<Integer> right)
    {
        left = Polynomial<Integer>.Zero;
        right = Polynomial<Integer>.Zero;
        if (f.Degree <= 1)
            return false;

        int acceptedPrimeIndex = 0;
        int maxPrimes = ComputePrimeBudget(f.Degree);
        foreach (int p in EnumerateGoodRecombinationPrimes(f, diagnostics, maxPrimes))
        {
            int primeIndex = acceptedPrimeIndex++;
            var rawModPoly = ToModCoefficients(f, p);
            int leadingMod = rawModPoly[^1];
            var modPoly = Monic(rawModPoly, p);
            if (Degree(modPoly) <= 1)
                continue;

            var ffInput = FromModCoefficients(modPoly, p);
            var ffFactors = p <= 7 ? Berlekamp(ffInput, p) : CantorZassenhaus(ffInput, p);
            if (ffFactors.Count < 2)
                continue;

            var ffPieces = ffFactors
                .Select(x => Monic(ToModCoefficients(x, p), p))
                .Where(x => Degree(x) > 0)
                .ToList();
            if (ffPieces.Count < 2)
                continue;

            var bound = MignotteBoundUpper(f);
            var target = bound * (Integer)2;
            var modulusPow = (Integer)p;
            int precision = 1;
            while ((BigInteger)modulusPow <= (BigInteger)target)
            {
                modulusPow *= (Integer)p;
                precision++;
            }

            int maskBudget = ComputeMaskBudget(ffPieces.Count, f.Degree, primeIndex);
            foreach (var mask in EnumerateSubsetMasks(ffPieces, targetDegree: f.Degree / 2, maxMasks: maskBudget))
            {
                diagnostics.SubsetMasksTried++;
                var g0 = new[] { 1 };
                var h0 = new[] { 1 };
                for (int i = 0; i < ffPieces.Count; i++)
                {
                    if (((mask >> i) & 1) == 1)
                        g0 = MulMod(g0, ffPieces[i], p);
                    else
                        h0 = MulMod(h0, ffPieces[i], p);
                }

                g0 = Monic(g0, p);
                h0 = Monic(h0, p);
                if (Degree(g0) <= 0 || Degree(h0) <= 0 || Degree(g0) + Degree(h0) != f.Degree)
                    continue;

                // Factors are computed for the monic-normalized polynomial; rescale to match f mod p.
                g0 = ScaleMod(g0, leadingMod, p);

                if (TryLiftedSplitCandidate(f, diagnostics, g0, h0, p, modulusPow, precision, out left, out right))
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<int> EnumerateGoodRecombinationPrimes(
        Polynomial<Integer> f,
        DiagnosticsAccumulator diagnostics,
        int maxPrimes)
    {
        int yielded = 0;
        foreach (var p in CandidatePrimes)
        {
            if (yielded >= maxPrimes)
                yield break;

            diagnostics.PrimeAttempts++;
            if (NormalizeBigMod((BigInteger)f.LeadingCoefficient, p) == 0)
                continue;

            var modPoly = Monic(ToModCoefficients(f, p), p);
            if (Degree(modPoly) <= 1)
                continue;

            var derivative = DerivativeMod(modPoly, p);
            if (Degree(GcdMod(modPoly, derivative, p)) > 0)
                continue;

            diagnostics.PrimeAccepted++;
            yield return p;
            yielded++;
        }
    }

    private static IEnumerable<Integer> IntegerRootCandidates(Polynomial<Integer> f)
    {
        var c = f[0];
        if (c.IsZero)
        {
            yield return Integer.Zero;
            yield break;
        }

        var abs = c.Abs();
        var absBig = (BigInteger)abs;
        if (absBig > int.MaxValue)
        {
            yield return Integer.One;
            yield return -Integer.One;
            yield break;
        }

        var n = (int)absBig;
        for (int d = 1; d * d <= n; d++)
        {
            if (n % d != 0)
                continue;

            yield return (Integer)d;
            yield return -(Integer)d;

            int q = n / d;
            if (q != d)
            {
                yield return (Integer)q;
                yield return -(Integer)q;
            }
        }
    }

    private static bool TryDivideByLinear(Polynomial<Integer> f, Integer root, out Polynomial<Integer> quotient)
    {
        if (f.Degree <= 0)
        {
            quotient = Polynomial<Integer>.Zero;
            return false;
        }

        int n = f.Degree;
        var a = new Integer[n + 1];
        for (int i = 0; i <= n; i++)
            a[i] = f[i];

        var q = new Integer[n];
        var carry = a[n];
        q[n - 1] = carry;
        for (int i = n - 1; i >= 1; i--)
        {
            carry = a[i] + root * carry;
            q[i - 1] = carry;
        }

        var remainder = a[0] + root * carry;
        if (!remainder.IsZero)
        {
            quotient = Polynomial<Integer>.Zero;
            return false;
        }

        quotient = NormalizePrimitive(Polynomial<Integer>.FromCoeffs(q));
        return true;
    }

    private static Polynomial<Integer> DivideByLinearExact(Polynomial<Integer> f, Integer root)
    {
        if (!TryDivideByLinear(f, root, out var quotient))
            throw new InvalidOperationException("Expected exact linear division.");
        return quotient;
    }

    private static Polynomial<Rational> DivideExact(Polynomial<Rational> a, Polynomial<Rational> b)
    {
        var (q, r) = a.DivMod(b);
        if (!r.IsZero)
            throw new InvalidOperationException("Expected exact polynomial division.");
        return q;
    }

    private static Polynomial<Rational> ToRational(Polynomial<Integer> p)
    {
        if (p.IsZero)
            return Polynomial<Rational>.Zero;

        var coeffs = new Rational[p.Degree + 1];
        for (int i = 0; i <= p.Degree; i++)
            coeffs[i] = Rational.FromInteger(p[i]);
        return Polynomial<Rational>.FromCoeffs(coeffs);
    }

    private static Polynomial<Integer> ToIntegerPrimitive(Polynomial<Rational> p)
    {
        if (p.IsZero)
            return Polynomial<Integer>.Zero;

        var lcm = Integer.One;
        foreach (var exp in p.Support)
            lcm = Integer.Lcm(lcm, p[exp].Denominator);

        var coeffs = new Integer[p.Degree + 1];
        for (int i = 0; i <= p.Degree; i++)
        {
            var c = p[i];
            var scaled = c.Numerator * (lcm / c.Denominator);
            coeffs[i] = scaled;
        }

        return NormalizePrimitive(PolynomialContent.PrimitivePart(Polynomial<Integer>.FromCoeffs(coeffs)));
    }

    private static Polynomial<Integer> ClearDenominators(Polynomial<Rational> p, out Integer lcm)
    {
        lcm = Integer.One;
        foreach (var exp in p.Support)
            lcm = Integer.Lcm(lcm, p[exp].Denominator);

        if (p.IsZero)
            return Polynomial<Integer>.Zero;

        var coeffs = new Integer[p.Degree + 1];
        for (int i = 0; i <= p.Degree; i++)
        {
            var c = p[i];
            coeffs[i] = c.Numerator * (lcm / c.Denominator);
        }

        return Polynomial<Integer>.FromCoeffs(coeffs);
    }

    private static Polynomial<Integer> NormalizePrimitive(Polynomial<Integer> p)
    {
        if (p.IsZero)
            return p;

        var primitive = PolynomialContent.PrimitivePart(p);
        if (primitive.LeadingCoefficient.Sign < 0)
            return -primitive;
        return primitive;
    }

    private static bool IsOne(Polynomial<Rational> p) =>
        p.Degree == 0 && p[0] == Rational.One;

    private static List<(Polynomial<Integer> Factor, int Multiplicity)> MergeMultiplicities(
        List<(Polynomial<Integer> Factor, int Multiplicity)> factors)
    {
        var merged = new List<(Polynomial<Integer> Factor, int Multiplicity)>();
        foreach (var (factor, multiplicity) in factors)
        {
            int index = merged.FindIndex(x => x.Factor == factor);
            if (index < 0)
            {
                merged.Add((factor, multiplicity));
            }
            else
            {
                merged[index] = (merged[index].Factor, merged[index].Multiplicity + multiplicity);
            }
        }

        return merged;
    }

    private static int[] ToModCoefficients(Polynomial<QuotientRing<Integer>> f, int p)
    {
        var coeffs = new int[f.Degree + 1];
        for (int i = 0; i <= f.Degree; i++)
        {
            coeffs[i] = NormalizeBigMod((BigInteger)f[i].Representative, p);
        }

        return TrimTrailingZeros(coeffs);
    }

    private static int[] ToModCoefficients(Polynomial<Integer> f, int p)
    {
        if (f.IsZero)
            return [0];

        var coeffs = new int[f.Degree + 1];
        for (int i = 0; i <= f.Degree; i++)
            coeffs[i] = NormalizeBigMod((BigInteger)f[i], p);

        return TrimTrailingZeros(coeffs);
    }

    private static int[] DerivativeMod(int[] poly, int p)
    {
        poly = TrimTrailingZeros(poly);
        if (poly.Length <= 1)
            return [0];

        var result = new int[poly.Length - 1];
        for (int i = 1; i < poly.Length; i++)
            result[i - 1] = NormalizeMod(i * poly[i], p);
        return TrimTrailingZeros(result);
    }

    private static List<(int[] Factor, int Multiplicity)> SquareFreeFactorizationMod(int[] f, int p)
    {
        f = Monic(f, p);
        if (Degree(f) <= 0)
            return [];

        var derivative = DerivativeMod(f, p);
        if (IsZeroPoly(derivative))
        {
            var root = PthRootMod(f, p);
            return SquareFreeFactorizationMod(root, p)
                .Select(x => (x.Factor, checked(x.Multiplicity * p)))
                .ToList();
        }

        var result = new List<(int[] Factor, int Multiplicity)>();
        var g = GcdMod(f, derivative, p);
        var w = DivideExactMod(f, g, p);
        int i = 1;

        while (!IsOnePoly(w))
        {
            var y = GcdMod(w, g, p);
            var z = DivideExactMod(w, y, p);
            if (Degree(z) > 0)
                result.Add((Monic(z, p), i));

            w = y;
            g = DivideExactMod(g, y, p);
            i++;
        }

        if (!IsOnePoly(g))
        {
            var root = PthRootMod(g, p);
            foreach (var (factor, multiplicity) in SquareFreeFactorizationMod(root, p))
                result.Add((factor, checked(multiplicity * p)));
        }

        return result;
    }

    private static int[] PthRootMod(int[] f, int p)
    {
        f = TrimTrailingZeros(f);
        if (IsZeroPoly(f))
            return [0];

        int degree = Degree(f);
        var root = new int[(degree / p) + 1];
        for (int i = 0; i < f.Length; i++)
        {
            int coeff = f[i];
            if (coeff == 0)
                continue;

            if (i % p != 0)
                throw new InvalidOperationException("Polynomial is not a p-th power in finite field square-free decomposition.");

            root[i / p] = coeff;
        }

        return TrimTrailingZeros(root);
    }

    private static int[] DivideExactMod(int[] dividend, int[] divisor, int p)
    {
        var (q, r) = DivRemMod(dividend, divisor, p);
        if (!IsZeroPoly(r))
            throw new InvalidOperationException("Expected exact modular polynomial division.");
        return TrimTrailingZeros(q);
    }

    private static bool IsOnePoly(int[] poly)
    {
        poly = TrimTrailingZeros(poly);
        return poly.Length == 1 && poly[0] == 1;
    }

    private static IEnumerable<int> EnumerateSubsetMasks(
        List<int[]> factors,
        int targetDegree,
        int maxMasks)
    {
        int count = factors.Count;
        if (count < 2 || count >= 31)
            yield break;

        int fullMask = (1 << count) - 1;
        int[] degrees = factors.Select(Degree).ToArray();
        int totalDegree = degrees.Sum();
        int[] suffixDegree = new int[count + 1];
        for (int i = count - 1; i >= 0; i--)
            suffixDegree[i] = suffixDegree[i + 1] + degrees[i];

        int degreeWindow = Math.Max(1, Math.Max(targetDegree / 2, totalDegree / 4));
        int minDegree = Math.Max(1, targetDegree - degreeWindow);
        int maxDegree = Math.Min(totalDegree - 1, targetDegree + degreeWindow);
        int poolLimit = Math.Max(maxMasks * 6, 96);
        var scoredMasks = new List<(int Mask, int Score, int SizeScore, int DegreeScore)>(poolLimit);

        void AddCandidate(int mask, int degreeSum, int sizeSum)
        {
            int balanceScore = Math.Abs(totalDegree - (2 * degreeSum));
            int degreeScore = Math.Abs(targetDegree - degreeSum);
            int sizeScore = Math.Abs((count / 2) - sizeSum);
            int score = (degreeScore * 1000) + (balanceScore * 10) + sizeScore;
            scoredMasks.Add((mask, score, sizeScore, degreeScore));
            if (scoredMasks.Count > poolLimit * 2)
            {
                scoredMasks.Sort((a, b) =>
                {
                    int c1 = a.Score.CompareTo(b.Score);
                    if (c1 != 0) return c1;
                    int c2 = a.SizeScore.CompareTo(b.SizeScore);
                    if (c2 != 0) return c2;
                    int c3 = a.DegreeScore.CompareTo(b.DegreeScore);
                    if (c3 != 0) return c3;
                    return a.Mask.CompareTo(b.Mask);
                });
                scoredMasks.RemoveRange(poolLimit, scoredMasks.Count - poolLimit);
            }
        }

        void Search(int index, int mask, int degreeSum, int sizeSum)
        {
            if (degreeSum > maxDegree)
                return;

            int remainingDegree = suffixDegree[index];
            if (degreeSum + remainingDegree < minDegree)
                return;

            if (index == count)
            {
                if ((mask & 1) == 0)
                    return;
                int complement = fullMask ^ mask;
                if (mask == 0 || complement == 0)
                    return;
                if (degreeSum < minDegree || degreeSum > maxDegree)
                    return;

                AddCandidate(mask, degreeSum, sizeSum);
                return;
            }

            Search(index + 1, mask, degreeSum, sizeSum);
            Search(index + 1, mask | (1 << index), degreeSum + degrees[index], sizeSum + 1);
        }

        Search(0, 0, 0, 0);

        foreach (var item in scoredMasks
            .OrderBy(x => x.Score)
            .ThenBy(x => x.SizeScore)
            .ThenBy(x => x.DegreeScore)
            .ThenBy(x => x.Mask)
            .Take(maxMasks))
        {
            yield return item.Mask;
        }
    }

    private static int ComputeMaskBudget(int factorCount, int degree)
    {
        int baseBudget = factorCount switch
        {
            <= 4 => 80,
            <= 6 => 160,
            <= 8 => 320,
            <= 10 => 480,
            _ => 640
        };

        if (degree >= 16)
            baseBudget += 160;
        if (degree >= 24)
            baseBudget += 160;
        return baseBudget;
    }

    private static int ComputeMaskBudget(int factorCount, int degree, int acceptedPrimeIndex)
    {
        int baseBudget = ComputeMaskBudget(factorCount, degree);
        return acceptedPrimeIndex switch
        {
            0 => Math.Max(96, baseBudget / 2),
            1 => Math.Max(128, (baseBudget * 3) / 4),
            _ => baseBudget
        };
    }

    private static int ComputePrimeBudget(int degree) => degree switch
    {
        <= 8 => 16,
        <= 16 => 24,
        <= 24 => 28,
        _ => 32
    };

    private static Polynomial<QuotientRing<Integer>> FromModCoefficients(int[] coeffs, int p)
    {
        if (coeffs.Length == 0)
            return Polynomial<QuotientRing<Integer>>.Zero;

        var values = new QuotientRing<Integer>[coeffs.Length];
        for (int i = 0; i < coeffs.Length; i++)
            values[i] = ZMod.Create((Integer)coeffs[i], (Integer)p);
        return Polynomial<QuotientRing<Integer>>.FromCoeffs(values);
    }

    private static Polynomial<QuotientRing<Integer>> LinearFactor(int root, int p)
    {
        // x - root over Z/pZ
        var constant = NormalizeMod(-root, p);
        return FromModCoefficients([constant, 1], p);
    }

    private static int EvaluateMod(int[] coeffs, int x, int p)
    {
        int result = 0;
        int power = 1;
        for (int i = 0; i < coeffs.Length; i++)
        {
            result = NormalizeMod(result + coeffs[i] * power, p);
            power = NormalizeMod(power * x, p);
        }

        return result;
    }

    private static int[] DivideByLinearMod(int[] coeffs, int root, int p)
    {
        int n = coeffs.Length - 1;
        if (n <= 0)
            return [0];

        var q = new int[n];
        int carry = NormalizeMod(coeffs[n], p);
        q[n - 1] = carry;
        for (int i = n - 1; i >= 1; i--)
        {
            carry = NormalizeMod(coeffs[i] + root * carry, p);
            q[i - 1] = carry;
        }

        int remainder = NormalizeMod(coeffs[0] + root * carry, p);
        if (remainder != 0)
            throw new InvalidOperationException("Expected exact linear division over finite field.");

        return TrimTrailingZeros(q);
    }

    private static int[] TrimTrailingZeros(int[] coeffs)
    {
        int last = coeffs.Length - 1;
        while (last >= 0 && coeffs[last] == 0)
            last--;

        if (last < 0)
            return [0];

        var trimmed = new int[last + 1];
        Array.Copy(coeffs, trimmed, last + 1);
        return trimmed;
    }

    private static int NormalizeMod(int x, int p)
    {
        int r = x % p;
        return r < 0 ? r + p : r;
    }

    private static int NormalizeBigMod(BigInteger x, int p)
    {
        var r = x % p;
        if (r < BigInteger.Zero)
            r += p;
        return (int)r;
    }

    private static int Degree(int[] poly) => poly.Length - 1;

    private static int[] Monic(int[] poly, int p)
    {
        poly = TrimTrailingZeros(poly);
        if (poly.Length == 0 || (poly.Length == 1 && poly[0] == 0))
            return [0];

        int inv = ModInverse(poly[^1], p);
        var result = new int[poly.Length];
        for (int i = 0; i < poly.Length; i++)
            result[i] = NormalizeMod(poly[i] * inv, p);
        return TrimTrailingZeros(result);
    }

    private static int[] AddMod(int[] a, int[] b, int p)
    {
        int n = Math.Max(a.Length, b.Length);
        var result = new int[n];
        for (int i = 0; i < n; i++)
        {
            int av = i < a.Length ? a[i] : 0;
            int bv = i < b.Length ? b[i] : 0;
            result[i] = NormalizeMod(av + bv, p);
        }
        return TrimTrailingZeros(result);
    }

    private static int[] SubMod(int[] a, int[] b, int p)
    {
        int n = Math.Max(a.Length, b.Length);
        var result = new int[n];
        for (int i = 0; i < n; i++)
        {
            int av = i < a.Length ? a[i] : 0;
            int bv = i < b.Length ? b[i] : 0;
            result[i] = NormalizeMod(av - bv, p);
        }
        return TrimTrailingZeros(result);
    }

    private static int[] MulMod(int[] a, int[] b, int p)
    {
        if (IsZeroPoly(a) || IsZeroPoly(b))
            return [0];

        var result = new int[a.Length + b.Length - 1];
        for (int i = 0; i < a.Length; i++)
        {
            for (int j = 0; j < b.Length; j++)
            {
                result[i + j] = NormalizeMod(result[i + j] + a[i] * b[j], p);
            }
        }
        return TrimTrailingZeros(result);
    }

    private static int[] PowMod(int[] @base, int exponent, int[] modulus, int p)
    {
        var result = new[] { 1 };
        var power = TrimTrailingZeros(@base);
        int e = exponent;
        while (e > 0)
        {
            if ((e & 1) == 1)
            {
                var mul = MulMod(result, power, p);
                result = DivRemMod(mul, modulus, p).Remainder;
            }

            e >>= 1;
            if (e > 0)
            {
                var sq = MulMod(power, power, p);
                power = DivRemMod(sq, modulus, p).Remainder;
            }
        }
        return TrimTrailingZeros(result);
    }

    private static int[] PowMod(int[] @base, long exponent, int[] modulus, int p)
    {
        var result = new[] { 1 };
        var power = TrimTrailingZeros(@base);
        long e = exponent;
        while (e > 0)
        {
            if ((e & 1L) == 1L)
            {
                var mul = MulMod(result, power, p);
                result = DivRemMod(mul, modulus, p).Remainder;
            }

            e >>= 1;
            if (e > 0)
            {
                var sq = MulMod(power, power, p);
                power = DivRemMod(sq, modulus, p).Remainder;
            }
        }
        return TrimTrailingZeros(result);
    }

    private static (int[] Quotient, int[] Remainder) DivRemMod(int[] dividend, int[] divisor, int p)
    {
        dividend = TrimTrailingZeros(dividend);
        divisor = TrimTrailingZeros(divisor);
        if (IsZeroPoly(divisor))
            throw new DivideByZeroException();

        if (Degree(dividend) < Degree(divisor))
            return ([0], dividend);

        var remainder = new int[dividend.Length];
        Array.Copy(dividend, remainder, dividend.Length);
        var quotient = new int[Math.Max(1, Degree(dividend) - Degree(divisor) + 1)];

        int divisorDegree = Degree(divisor);
        int divisorLeadInv = ModInverse(divisor[^1], p);

        while (Degree(TrimTrailingZeros(remainder)) >= divisorDegree && !IsZeroPoly(remainder))
        {
            remainder = TrimTrailingZeros(remainder);
            int remDegree = Degree(remainder);
            int degreeDiff = remDegree - divisorDegree;
            int scale = NormalizeMod(remainder[^1] * divisorLeadInv, p);
            quotient[degreeDiff] = NormalizeMod(quotient[degreeDiff] + scale, p);

            for (int i = 0; i <= divisorDegree; i++)
            {
                int index = i + degreeDiff;
                remainder[index] = NormalizeMod(remainder[index] - scale * divisor[i], p);
            }
        }

        return (TrimTrailingZeros(quotient), TrimTrailingZeros(remainder));
    }

    private static int[] GcdMod(int[] a, int[] b, int p)
    {
        a = TrimTrailingZeros(a);
        b = TrimTrailingZeros(b);
        while (!IsZeroPoly(b))
        {
            var (_, r) = DivRemMod(a, b, p);
            a = b;
            b = r;
        }

        return Monic(a, p);
    }

    private static bool IsZeroPoly(int[] poly) =>
        poly.Length == 0 || (poly.Length == 1 && poly[0] == 0);

    private static int ModInverse(int value, int p)
    {
        int a = NormalizeMod(value, p);
        if (a == 0)
            throw new DivideByZeroException("No modular inverse for zero.");

        int t = 0, newT = 1;
        int r = p, newR = a;
        while (newR != 0)
        {
            int q = r / newR;
            (t, newT) = (newT, t - q * newT);
            (r, newR) = (newR, r - q * newR);
        }

        if (r != 1)
            throw new InvalidOperationException("Modulus must be prime for inversion in finite-field algorithms.");

        return NormalizeMod(t, p);
    }

    private static (int[] Left, int[] Right)? BerlekampSplitOnce(int[] f, int p)
    {
        f = Monic(f, p);
        int n = Degree(f);
        if (n <= 1)
            return null;

        var matrix = new int[n, n];
        var xPoly = new[] { 0, 1 };
        for (int col = 0; col < n; col++)
        {
            var power = PowMod(xPoly, p * col, f, p);
            for (int row = 0; row < n; row++)
            {
                int coeff = row < power.Length ? power[row] : 0;
                matrix[row, col] = coeff;
            }
            matrix[col, col] = NormalizeMod(matrix[col, col] - 1, p);
        }

        var basis = NullSpaceBasis(matrix, p);
        if (basis.Count <= 1)
            return null;

        foreach (var vec in basis)
        {
            bool scalar = true;
            for (int i = 1; i < vec.Length; i++)
            {
                if (vec[i] != 0)
                {
                    scalar = false;
                    break;
                }
            }

            if (scalar)
                continue;

            var g = TrimTrailingZeros(vec);
            var (_, gMod) = DivRemMod(g, f, p);
            for (int a = 0; a < p; a++)
            {
                var shifted = (int[])gMod.Clone();
                if (shifted.Length == 0)
                    shifted = [0];
                shifted[0] = NormalizeMod(shifted[0] - a, p);

                var d = GcdMod(f, shifted, p);
                int dDeg = Degree(d);
                if (dDeg <= 0 || dDeg >= n)
                    continue;

                var (q, r) = DivRemMod(f, d, p);
                if (!IsZeroPoly(r))
                    continue;

                return (Monic(d, p), Monic(q, p));
            }
        }

        return null;
    }

    private static List<(int[] Factor, int DegreeD)> DistinctDegreeFactorization(int[] f, int p)
    {
        var result = new List<(int[] Factor, int DegreeD)>();
        var remaining = Monic(f, p);
        if (Degree(remaining) <= 0)
            return result;

        var x = new[] { 0, 1 };
        var h = (int[])x.Clone(); // h = x^(p^d) mod remaining, updated per d
        int d = 1;

        while (2 * d <= Degree(remaining))
        {
            h = PowMod(h, p, remaining, p); // Frobenius: raise to p-th power
            var candidate = SubMod(h, x, p);
            var g = GcdMod(remaining, candidate, p);
            int gDeg = Degree(g);
            if (gDeg > 0)
            {
                result.Add((Monic(g, p), d));
                var (q, r) = DivRemMod(remaining, g, p);
                if (!IsZeroPoly(r))
                    throw new InvalidOperationException("Expected exact division in distinct-degree factorization.");

                remaining = Monic(q, p);
                if (Degree(remaining) <= 0)
                    return result;

                h = DivRemMod(h, remaining, p).Remainder;
            }

            d++;
        }

        if (Degree(remaining) > 0)
            result.Add((Monic(remaining, p), Degree(remaining)));

        return result;
    }

    private static List<int[]> EqualDegreeFactorization(int[] f, int d, int p)
    {
        f = Monic(f, p);
        int n = Degree(f);
        if (n <= d)
            return [f];

        if (p == 2)
        {
            // Characteristic 2 equal-degree split is less convenient with the odd-prime formula.
            // Fall back to Berlekamp splitting which is deterministic and already implemented.
            var split2 = BerlekampSplitOnce(f, p);
            if (split2 is null)
                return [f];

            var left2 = EqualDegreeFactorization(split2.Value.Left, d, p);
            var right2 = EqualDegreeFactorization(split2.Value.Right, d, p);
            left2.AddRange(right2);
            return left2;
        }

        long pd = PowIntChecked(p, d);
        long exponent = (pd - 1) / 2;
        if (exponent <= 0)
            return [f];

        // Deterministic pseudo-random sequence for reproducible tests.
        for (int seed = 1; seed <= 64; seed++)
        {
            var a = RandomPoly(seed, Math.Max(1, n - 1), p);
            var pow = PowMod(a, exponent, f, p);
            var shifted = SubMod(pow, [1], p);
            var g = GcdMod(f, shifted, p);
            int gDeg = Degree(g);
            if (gDeg <= 0 || gDeg >= n)
                continue;

            var (q, r) = DivRemMod(f, g, p);
            if (!IsZeroPoly(r))
                continue;

            var left = EqualDegreeFactorization(Monic(g, p), d, p);
            var right = EqualDegreeFactorization(Monic(q, p), d, p);
            left.AddRange(right);
            return left;
        }

        var split = BerlekampSplitOnce(f, p);
        if (split is null)
            return [f];

        var leftFallback = EqualDegreeFactorization(split.Value.Left, d, p);
        var rightFallback = EqualDegreeFactorization(split.Value.Right, d, p);
        leftFallback.AddRange(rightFallback);
        return leftFallback;
    }

    private static int[] RandomPoly(int seed, int maxDegree, int p)
    {
        var coeffs = new int[maxDegree + 1];
        uint state = (uint)(seed * 747796405 + 2891336453);
        for (int i = 0; i < coeffs.Length; i++)
        {
            state = state * 1664525u + 1013904223u;
            coeffs[i] = (int)(state % (uint)p);
        }

        if (IsZeroPoly(coeffs))
            coeffs[0] = 1;

        return TrimTrailingZeros(coeffs);
    }

    private static long PowIntChecked(int @base, int exponent)
    {
        long result = 1;
        for (int i = 0; i < exponent; i++)
        {
            checked { result *= @base; }
        }
        return result;
    }

    private static List<int[]> NullSpaceBasis(int[,] matrix, int p)
    {
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        var a = (int[,])matrix.Clone();
        var pivotCols = new List<int>();
        int r = 0;

        for (int c = 0; c < cols && r < rows; c++)
        {
            int pivot = -1;
            for (int i = r; i < rows; i++)
            {
                if (NormalizeMod(a[i, c], p) != 0)
                {
                    pivot = i;
                    break;
                }
            }

            if (pivot < 0)
                continue;

            if (pivot != r)
            {
                for (int j = c; j < cols; j++)
                    (a[r, j], a[pivot, j]) = (a[pivot, j], a[r, j]);
            }

            int inv = ModInverse(a[r, c], p);
            for (int j = c; j < cols; j++)
                a[r, j] = NormalizeMod(a[r, j] * inv, p);

            for (int i = 0; i < rows; i++)
            {
                if (i == r)
                    continue;

                int factor = NormalizeMod(a[i, c], p);
                if (factor == 0)
                    continue;

                for (int j = c; j < cols; j++)
                    a[i, j] = NormalizeMod(a[i, j] - factor * a[r, j], p);
            }

            pivotCols.Add(c);
            r++;
        }

        var freeCols = new List<int>();
        for (int c = 0; c < cols; c++)
            if (!pivotCols.Contains(c))
                freeCols.Add(c);

        var basis = new List<int[]>();
        if (freeCols.Count == 0)
        {
            var v = new int[cols];
            v[0] = 1;
            basis.Add(v);
            return basis;
        }

        foreach (var free in freeCols)
        {
            var vec = new int[cols];
            vec[free] = 1;
            for (int row = 0; row < pivotCols.Count; row++)
            {
                int pivotCol = pivotCols[row];
                vec[pivotCol] = NormalizeMod(-a[row, free], p);
            }
            basis.Add(vec);
        }

        return basis;
    }

    private static (int[] Gcd, int[] S, int[] T) ExtendedGcdMod(int[] a, int[] b, int p)
    {
        var oldR = TrimTrailingZeros(a);
        var r = TrimTrailingZeros(b);
        var oldS = new[] { 1 };
        var s = new[] { 0 };
        var oldT = new[] { 0 };
        var t = new[] { 1 };

        while (!IsZeroPoly(r))
        {
            var (q, rem) = DivRemMod(oldR, r, p);
            (oldR, r) = (r, rem);
            (oldS, s) = (s, SubMod(oldS, MulMod(q, s, p), p));
            (oldT, t) = (t, SubMod(oldT, MulMod(q, t, p), p));
        }

        oldR = Monic(oldR, p);
        if (IsZeroPoly(oldR))
            return (oldR, oldS, oldT);

        int inv = ModInverse(oldR[^1], p);
        oldR = ScaleMod(oldR, inv, p);
        oldS = ScaleMod(oldS, inv, p);
        oldT = ScaleMod(oldT, inv, p);
        return (oldR, oldS, oldT);
    }

    private static bool TryLiftedSplitCandidate(
        Polynomial<Integer> f,
        DiagnosticsAccumulator diagnostics,
        int[] g0,
        int[] h0,
        int p,
        Integer modulusPow,
        int precision,
        out Polynomial<Integer> left,
        out Polynomial<Integer> right)
    {
        left = Polynomial<Integer>.Zero;
        right = Polynomial<Integer>.Zero;
        diagnostics.HenselLiftAttempts++;

        var gSeed = FromSmallModCoefficients(g0);
        var hSeed = FromSmallModCoefficients(h0);
        var (gLift, hLift) = HenselLift(f, gSeed, hSeed, (Integer)p, precision);

        var gCandidate = NormalizePrimitive(CenterCoefficientsMod(gLift, modulusPow));
        if (TryDivideExactOverZ(f, gCandidate, out var qCandidate))
        {
            left = NormalizePrimitive(gCandidate);
            right = NormalizePrimitive(qCandidate);
            if (left.Degree > 0 && right.Degree > 0 && left.Degree < f.Degree && right.Degree < f.Degree)
            {
                diagnostics.HenselLiftSuccesses++;
                return true;
            }
        }

        var hCandidate = NormalizePrimitive(CenterCoefficientsMod(hLift, modulusPow));
        if (TryDivideExactOverZ(f, hCandidate, out qCandidate))
        {
            left = NormalizePrimitive(hCandidate);
            right = NormalizePrimitive(qCandidate);
            if (left.Degree > 0 && right.Degree > 0 && left.Degree < f.Degree && right.Degree < f.Degree)
            {
                diagnostics.HenselLiftSuccesses++;
                return true;
            }
        }

        return false;
    }

    private static Integer MignotteBoundUpper(Polynomial<Integer> f)
    {
        if (f.IsZero)
            return Integer.Zero;

        int n = f.Degree;
        BigInteger l1 = BigInteger.Zero;
        for (int i = 0; i <= n; i++)
            l1 += BigInteger.Abs((BigInteger)f[i]);

        var binomial = Binomial(n, n / 2);
        var bound = binomial * l1;
        return bound <= BigInteger.Zero ? Integer.One : (Integer)bound;
    }

    private static BigInteger Binomial(int n, int k)
    {
        if (k < 0 || k > n)
            return BigInteger.Zero;
        if (k == 0 || k == n)
            return BigInteger.One;

        k = Math.Min(k, n - k);
        BigInteger result = BigInteger.One;
        for (int i = 1; i <= k; i++)
            result = (result * (n - k + i)) / i;
        return result;
    }

    private static Polynomial<Integer> CenterCoefficientsMod(Polynomial<Integer> poly, Integer modulus)
    {
        if (poly.IsZero)
            return Polynomial<Integer>.Zero;

        var modBig = BigInteger.Abs((BigInteger)modulus);
        if (modBig <= BigInteger.One)
            return poly;

        var half = modBig / 2;
        var coeffs = new Integer[poly.Degree + 1];
        for (int i = 0; i <= poly.Degree; i++)
        {
            var c = ((BigInteger)poly[i]) % modBig;
            if (c < BigInteger.Zero)
                c += modBig;
            if (c > half)
                c -= modBig;
            coeffs[i] = (Integer)c;
        }

        return Polynomial<Integer>.FromCoeffs(coeffs);
    }

    private static bool TryDivideExactOverZ(
        Polynomial<Integer> dividend,
        Polynomial<Integer> divisor,
        out Polynomial<Integer> quotient)
    {
        quotient = Polynomial<Integer>.Zero;
        if (divisor.IsZero || divisor.Degree <= 0 || divisor.Degree >= dividend.Degree)
            return false;

        var (qRat, rRat) = ToRational(dividend).DivMod(ToRational(divisor));
        if (!rRat.IsZero)
            return false;

        var coeffs = new Integer[qRat.Degree + 1];
        for (int i = 0; i <= qRat.Degree; i++)
        {
            var c = qRat[i];
            if (c.Denominator != Integer.One)
                return false;
            coeffs[i] = c.Numerator;
        }

        quotient = Polynomial<Integer>.FromCoeffs(coeffs);
        return divisor * quotient == dividend;
    }

    private static int[] ScaleMod(int[] poly, int scalar, int p)
    {
        var result = new int[poly.Length];
        for (int i = 0; i < poly.Length; i++)
            result[i] = NormalizeMod(poly[i] * scalar, p);
        return TrimTrailingZeros(result);
    }

    private static Polynomial<Integer> ScaleByInteger(Polynomial<Integer> poly, Integer scalar)
    {
        if (poly.IsZero || scalar.IsZero)
            return Polynomial<Integer>.Zero;

        var coeffs = new Integer[Math.Max(0, poly.Degree + 1)];
        for (int i = 0; i <= poly.Degree; i++)
            coeffs[i] = poly[i] * scalar;
        return Polynomial<Integer>.FromCoeffs(coeffs);
    }

    private static Polynomial<Integer> DivideByScalarPower(Polynomial<Integer> poly, Integer divisor)
    {
        if (poly.IsZero)
            return Polynomial<Integer>.Zero;

        var coeffs = new Integer[Math.Max(0, poly.Degree + 1)];
        for (int i = 0; i <= poly.Degree; i++)
        {
            var (q, _) = Integer.DivMod(poly[i], divisor);
            coeffs[i] = q;
        }
        return Polynomial<Integer>.FromCoeffs(coeffs);
    }

    private static Polynomial<Integer> FromSmallModCoefficients(int[] coeffs)
    {
        if (coeffs.Length == 0)
            return Polynomial<Integer>.Zero;

        var ints = new Integer[coeffs.Length];
        for (int i = 0; i < coeffs.Length; i++)
            ints[i] = (Integer)coeffs[i];
        return Polynomial<Integer>.FromCoeffs(ints);
    }

    private static readonly int[] CandidatePrimes =
    [
        3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47,
        53, 59, 61, 67, 71, 73, 79, 83, 89, 97, 101, 103, 107,
        109, 113, 127, 131, 137, 139, 149, 151, 157, 163, 167,
        173, 179, 181, 191, 193, 197, 199
    ];
}
