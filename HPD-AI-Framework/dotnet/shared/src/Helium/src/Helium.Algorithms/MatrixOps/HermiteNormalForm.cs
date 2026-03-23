using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algorithms;

/// <summary>
/// Hermite Normal Form of an integer matrix.
///
/// H is the unique upper-triangular matrix H = U * A for some unimodular U satisfying:
///   - H is upper triangular: H[i,j] = 0 for i > j
///   - Diagonal entries H[i,i] > 0 (when nonzero)
///   - Above-diagonal entries: 0 ≤ H[i,j] &lt; H[i,i] for j > i
///
/// Row operations are used throughout.
/// </summary>
public static class HermiteNormalForm
{
    /// <summary>
    /// Hermite Normal Form with a known determinant bound D.
    /// All intermediate entries are reduced modulo D after each row operation,
    /// keeping entry size bounded by log₂ D bits throughout.
    ///
    /// Algorithm: modular HNF (Domich-Kannan-Trotter 1987).
    /// Use this overload whenever D is available (e.g. D = disc(f) for maximal order).
    /// Exact: YES.
    /// </summary>
    public static Matrix<Integer> Compute(Matrix<Integer> A, Integer D)
    {
        int m = A.Rows;
        int n = A.Cols;
        var a = CopyToArray(A, m, n);
        var absD = D.Abs();

        int pivotRow = 0;

        for (int col = 0; col < n && pivotRow < m; col++)
        {
            // Find a nonzero entry in column col at or below pivotRow.
            int src = -1;
            for (int i = pivotRow; i < m; i++)
            {
                if (!a[i * n + col].IsZero) { src = i; break; }
            }
            if (src < 0) continue; // column is zero below pivot; move to next column

            // Bring the nonzero entry to pivotRow.
            if (src != pivotRow)
                SwapRows(a, n, src, pivotRow);

            // GCD-eliminate all other nonzero entries below pivotRow in this column.
            for (int i = pivotRow + 1; i < m; i++)
            {
                while (!a[i * n + col].IsZero)
                {
                    // Extended GCD step: keep pivot row updated.
                    var (g, u, v) = Gcd.Extended(a[pivotRow * n + col], a[i * n + col]);
                    var (qi, _)  = Integer.DivMod(a[i * n + col],       g);
                    var (qp, _)  = Integer.DivMod(a[pivotRow * n + col], g);

                    for (int c = col; c < n; c++)
                    {
                        var ep = a[pivotRow * n + c];
                        var ei = a[i * n + c];
                        var newP = u * ep + v * ei;
                        var newI = (-qi) * ep + qp * ei;
                        a[pivotRow * n + c] = absD.IsZero ? newP : PosMod(newP, absD);
                        a[i * n + c]        = absD.IsZero ? newI : PosMod(newI, absD);
                    }
                    // If row i is now zero in col, we're done with it.
                    if (a[i * n + col].IsZero) break;
                }
            }

            // Ensure pivot is positive.
            if (a[pivotRow * n + col].Sign < 0)
                NegateRow(a, n, pivotRow, col);

            var diag = a[pivotRow * n + col];

            // Reduce entries above the pivot in this column: 0 ≤ a[r,col] < diag.
            if (!diag.IsZero)
            {
                for (int r = 0; r < pivotRow; r++)
                {
                    var entry = a[r * n + col];
                    var reduced = PosMod(entry, diag);
                    if (reduced != entry)
                    {
                        var (q, _) = Integer.DivMod(entry - reduced, diag);
                        for (int c = col; c < n; c++)
                            a[r * n + c] = a[r * n + c] - q * a[pivotRow * n + c];
                    }
                }
            }

            pivotRow++;
        }

        // Final above-diagonal reduction pass.
        // Collect (pivotRow, pivotCol) pairs, then reduce right-to-left so that
        // reducing against a later pivot never disturbs an earlier one.
        var pivots = new List<(int Row, int Col)>();
        {
            int pr = 0;
            for (int col = 0; col < n && pr < m; col++)
            {
                if (!a[pr * n + col].IsZero) { pivots.Add((pr, col)); pr++; }
            }
        }
        for (int pi = pivots.Count - 1; pi >= 0; pi--)
        {
            var (pr, pc) = pivots[pi];
            var diag = a[pr * n + pc];
            if (diag.IsZero) continue;
            for (int r = 0; r < pr; r++)
            {
                var entry = a[r * n + pc];
                var reduced = PosMod(entry, diag);
                if (reduced != entry)
                {
                    var (q, _) = Integer.DivMod(entry - reduced, diag);
                    for (int c = pc; c < n; c++)
                        a[r * n + c] = a[r * n + c] - q * a[pr * n + c];
                }
            }
        }

        return Matrix<Integer>.FromArray(m, n, a);
    }

    /// <summary>
    /// Hermite Normal Form without a determinant bound.
    /// Uses classical row reduction with extended GCD.
    /// Exact: YES.
    /// </summary>
    public static Matrix<Integer> Compute(Matrix<Integer> A)
    {
        return Compute(A, Integer.Zero);
    }

    // -------------------------------------------------------------------------
    // Helpers (internal so SmithNormalForm can reuse)
    // -------------------------------------------------------------------------

    internal static Integer[] CopyToArray(Matrix<Integer> m, int rows, int cols)
    {
        var a = new Integer[rows * cols];
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                a[i * cols + j] = m[i, j];
        return a;
    }

    internal static void SwapRows(Integer[] a, int n, int r1, int r2)
    {
        for (int c = 0; c < n; c++)
            (a[r1 * n + c], a[r2 * n + c]) = (a[r2 * n + c], a[r1 * n + c]);
    }

    private static void NegateRow(Integer[] a, int n, int r, int fromCol)
    {
        for (int c = fromCol; c < n; c++)
            a[r * n + c] = -a[r * n + c];
    }

    internal static Integer PosMod(Integer x, Integer modulus)
    {
        if (modulus.IsZero) return x;
        var (_, r) = Integer.DivMod(x, modulus);
        if (r.Sign < 0) r += modulus;
        return r;
    }
}
