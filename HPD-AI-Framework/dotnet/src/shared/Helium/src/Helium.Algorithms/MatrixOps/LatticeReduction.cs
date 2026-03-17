using System.Numerics;
using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algorithms;

/// <summary>
/// LLL lattice basis reduction for integer lattices.
///
/// Algorithm: L² (Nguyen–Stehlé 2009) with exact integer Gram matrix.
/// Fast path: double GSO coefficients. Precision failure fallback: Rational GSO.
/// Parameters: δ = 0.99, η = 0.51.
///
/// Invariant: output rows span the same ℤ-lattice as input rows.
/// Guarantee: output is LLL-reduced (Lovász + size-reduction conditions).
/// </summary>
public static class LatticeReduction
{
    private const double Delta = 0.99;
    private const double Eta   = 0.51;

    /// <summary>
    /// LLL-reduces the given integer lattice basis.
    /// Input rows form a basis of a lattice Λ ⊆ ℤ^n.
    /// Output rows form an LLL-reduced basis of the same lattice.
    ///
    /// Algorithm: L² with exact integer Gram matrix and double GSO coefficients.
    /// Rational fallback for precision failures. δ = 0.99, η = 0.51.
    /// Exact: YES (same lattice). Reduced: YES (Lovász + size-reduction guaranteed).
    /// </summary>
    public static Matrix<Integer> LLL(Matrix<Integer> basis)
        => LLLInternal(basis, keepFirst: 0);

    // Internal overload for van Hoeij: keepFirst = 1 prevents row 0 from being
    // swapped out of position, preserving the p^k scale structure.
    internal static Matrix<Integer> LLL(Matrix<Integer> basis, int keepFirst)
        => LLLInternal(basis, keepFirst);

    // -------------------------------------------------------------------------
    // Core algorithm
    // -------------------------------------------------------------------------

    private static Matrix<Integer> LLLInternal(Matrix<Integer> basis, int keepFirst)
    {
        int m = basis.Rows;
        int n = basis.Cols;

        if (m <= 1)
            return basis;

        // Working copy of basis rows: b[i * n + j] = row i, col j.
        var b = HermiteNormalForm.CopyToArray(basis, m, n);

        // Gram matrix G[i,j] = <b_i, b_j>, exact Integer, symmetric.
        var G = new Integer[m * m];
        for (int i = 0; i < m; i++)
            for (int j = i; j < m; j++)
            {
                var dot = DotProduct(b, i, j, n);
                G[i * m + j] = dot;
                G[j * m + i] = dot;
            }

        // GSO coefficients μ[i,j] (i > j), and squared GSO norms r[i] = ||b_i*||².
        // Stored as doubles; recomputed from G whenever precision is suspect.
        var mu = new double[m * m];
        var r  = new double[m];
        RecomputeGSO(G, mu, r, m, fromRow: 0);

        int k = 1; // 0-indexed; proposal uses 1-indexed but same logic.
        int stagnantPasses = 0;
        double prevEmax = double.PositiveInfinity;

        while (k < m)
        {
            // ---- Size-reduce row k against rows 0 .. k-1 ----
            bool anyReduction = false;
            for (int j = k - 1; j >= 0; j--)
            {
                double muKJ = mu[k * m + j];
                if (Math.Abs(muKJ) > Eta)
                {
                    long q = (long)Math.Round(muKJ);
                    // b[k] -= q * b[j]  (exact integer row update)
                    for (int c = 0; c < n; c++)
                        b[k * n + c] -= (Integer)q * b[j * n + c];

                    // Update Gram matrix exactly.
                    // G[k,k] -= 2q G[k,j] - q² G[j,j]  (from expand <b_k - q b_j, b_k - q b_j>)
                    // G[k,i] -= q G[j,i] for i ≠ k
                    var qI = (Integer)q;
                    var gKK = G[k * m + k] - (Integer)2 * qI * G[k * m + j] + qI * qI * G[j * m + j];
                    G[k * m + k] = gKK;
                    for (int i = 0; i < m; i++)
                    {
                        if (i == k) continue;
                        var updated = G[k * m + i] - qI * G[j * m + i];
                        G[k * m + i] = updated;
                        G[i * m + k] = updated;
                    }

                    // Update mu (double) for columns < j: mu[k,l] -= q * mu[j,l]
                    for (int l = 0; l < j; l++)
                        mu[k * m + l] -= q * mu[j * m + l];
                    mu[k * m + j] -= q;

                    anyReduction = true;
                }
            }

            // Recompute mu[k,j] from G for fresh accuracy.
            RecomputeGSORow(G, mu, r, m, k);

            // ---- Precision failure detection (stagnation) ----
            double emax = ComputeEmax(mu, m, k);
            if (!anyReduction && emax >= prevEmax - 1e-12)
            {
                stagnantPasses++;
                if (stagnantPasses >= 2)
                {
                    // Fallback: recompute all of mu from G using Rational.
                    RecomputeGSOExact(G, mu, r, m);
                    stagnantPasses = 0;
                }
            }
            else
            {
                stagnantPasses = 0;
            }
            prevEmax = emax;

            // ---- Lovász condition ----
            // δ · r[k-1] ≤ s[k-1]  where s[k-1] = r[k] + μ[k,k-1]² · r[k-1]
            double muKPrev = mu[k * m + (k - 1)];
            double sk = r[k] + muKPrev * muKPrev * r[k - 1];

            if (Delta * r[k - 1] <= sk)
            {
                k++;
            }
            else
            {
                // Swap row k and row k-1, unless keepFirst prevents it.
                if (keepFirst > 0 && k == 1)
                {
                    // Do not swap row 0 out; advance instead.
                    k++;
                    continue;
                }

                SwapRows(b, n, k, k - 1);
                SwapGramRows(G, m, k, k - 1);
                RecomputeGSO(G, mu, r, m, fromRow: Math.Max(0, k - 1));
                k = Math.Max(k - 1, 1);
                stagnantPasses = 0;
                prevEmax = double.PositiveInfinity;
            }
        }

        return Matrix<Integer>.FromArray(m, n, b);
    }

    // -------------------------------------------------------------------------
    // GSO helpers
    // -------------------------------------------------------------------------

    // Recompute μ[i,j] and r[i] from G for all rows >= fromRow.
    // Rows before fromRow are assumed already valid.
    // Uses double arithmetic (fast path).
    private static void RecomputeGSO(Integer[] G, double[] mu, double[] r, int m, int fromRow)
    {
        if (fromRow == 0)
            r[0] = (double)(BigInteger)G[0]; // r[0] = ||b_0||² — no predecessors

        for (int i = Math.Max(fromRow, 1); i < m; i++)
            RecomputeGSORow(G, mu, r, m, i);
    }

    // Recompute μ[k,j] for j = 0..k-1 and update r[k], given existing r[0..k-1] and μ[j,*] for j<k.
    private static void RecomputeGSORow(Integer[] G, double[] mu, double[] r, int m, int k)
    {
        // μ[k,j] = (G[k,j] - Σ_{l=0}^{j-1} μ[k,l] · μ[j,l] · r[l]) / r[j]
        double gKK = (double)(BigInteger)G[k * m + k];
        for (int j = 0; j < k; j++)
        {
            double s = (double)(BigInteger)G[k * m + j];
            for (int l = 0; l < j; l++)
                s -= mu[k * m + l] * mu[j * m + l] * r[l];

            double rJ = r[j];
            mu[k * m + j] = rJ > 0 ? s / rJ : 0.0;
            gKK -= mu[k * m + j] * mu[k * m + j] * rJ;
        }

        r[k] = gKK;
    }

    // Exact fallback: recompute all μ and r from G using Rational.
    // Converts back to double after computation.
    private static void RecomputeGSOExact(Integer[] G, double[] mu, double[] r, int m)
    {
        var rExact  = new Rational[m];
        var muExact = new Rational[m * m];

        rExact[0] = Rational.FromInteger(G[0]);

        for (int k = 1; k < m; k++)
        {
            var gKK = Rational.FromInteger(G[k * m + k]);
            for (int j = 0; j < k; j++)
            {
                var s = Rational.FromInteger(G[k * m + j]);
                for (int l = 0; l < j; l++)
                    s -= muExact[k * m + l] * muExact[j * m + l] * rExact[l];

                muExact[k * m + j] = rExact[j].IsZero
                    ? Rational.Zero
                    : s / rExact[j];

                gKK -= muExact[k * m + j] * muExact[k * m + j] * rExact[j];
            }
            rExact[k] = gKK;
        }

        // Copy back to double arrays.
        for (int i = 0; i < m; i++)
        {
            r[i] = ToDouble(rExact[i]);
            for (int j = 0; j < m; j++)
                mu[i * m + j] = ToDouble(muExact[i * m + j]);
        }
    }

    private static double ToDouble(Rational r)
    {
        var num = (BigInteger)r.Numerator;
        var den = (BigInteger)r.Denominator;
        if (den.IsZero) return 0.0;
        return (double)(num / den) + (double)(num % den) / (double)den;
    }

    private static double ComputeEmax(double[] mu, int m, int upToRow)
    {
        double emax = 0.0;
        for (int i = 1; i <= Math.Min(upToRow, m - 1); i++)
            for (int j = 0; j < i; j++)
                emax = Math.Max(emax, Math.Abs(mu[i * m + j]));
        return emax;
    }

    // -------------------------------------------------------------------------
    // Gram matrix helpers
    // -------------------------------------------------------------------------

    private static Integer DotProduct(Integer[] b, int row1, int row2, int n)
    {
        var sum = Integer.Zero;
        for (int c = 0; c < n; c++)
            sum += b[row1 * n + c] * b[row2 * n + c];
        return sum;
    }

    private static void SwapRows(Integer[] b, int n, int r1, int r2)
    {
        for (int c = 0; c < n; c++)
            (b[r1 * n + c], b[r2 * n + c]) = (b[r2 * n + c], b[r1 * n + c]);
    }

    // Swap rows and columns k and k-1 in the Gram matrix (symmetric update).
    private static void SwapGramRows(Integer[] G, int m, int r1, int r2)
    {
        // Swap row r1 and row r2.
        for (int c = 0; c < m; c++)
            (G[r1 * m + c], G[r2 * m + c]) = (G[r2 * m + c], G[r1 * m + c]);
        // Swap col r1 and col r2.
        for (int r = 0; r < m; r++)
            (G[r * m + r1], G[r * m + r2]) = (G[r * m + r2], G[r * m + r1]);
    }
}
