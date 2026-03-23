using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algorithms;

/// <summary>
/// LU decomposition with partial pivoting for matrices over a field.
/// Decomposes A into P*A = L*U where L is lower triangular with 1s on diagonal,
/// U is upper triangular.
/// </summary>
public static class LUDecomposition
{
    /// <summary>
    /// Computes LU decomposition with partial pivoting.
    /// Returns (L, U, swapCount) or null if the matrix is singular.
    /// The permutation is tracked as a swap count for determinant sign.
    /// </summary>
    public static (Matrix<R> L, Matrix<R> U, int SwapCount)? Decompose<R>(Matrix<R> m)
        where R : IField<R>
    {
        int n = m.Rows;
        if (n != m.Cols)
            throw new ArgumentException("Matrix must be square.");

        // Mutable working copies.
        var u = new R[n, n];
        var l = new R[n, n];

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
                u[i, j] = m[i, j];
            l[i, i] = R.MultiplicativeIdentity;
        }

        int swaps = 0;

        for (int k = 0; k < n; k++)
        {
            // Find pivot.
            int pivotRow = -1;
            for (int i = k; i < n; i++)
            {
                if (!u[i, k].Equals(R.AdditiveIdentity))
                {
                    pivotRow = i;
                    break;
                }
            }

            if (pivotRow == -1)
                return null; // Singular.

            if (pivotRow != k)
            {
                // Swap rows in U.
                for (int j = 0; j < n; j++)
                    (u[k, j], u[pivotRow, j]) = (u[pivotRow, j], u[k, j]);
                // Swap the already-computed L entries (columns 0..k-1).
                for (int j = 0; j < k; j++)
                    (l[k, j], l[pivotRow, j]) = (l[pivotRow, j], l[k, j]);
                swaps++;
            }

            var pivotInv = R.Invert(u[k, k]);
            for (int i = k + 1; i < n; i++)
            {
                var factor = u[i, k] * pivotInv;
                l[i, k] = factor;
                for (int j = k; j < n; j++)
                    u[i, j] = u[i, j] - factor * u[k, j];
            }
        }

        // Convert to Matrix<R>.
        var lData = new R[n * n];
        var uData = new R[n * n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                lData[i * n + j] = l[i, j];
                uData[i * n + j] = u[i, j];
            }

        return (Matrix<R>.FromArray(n, n, lData), Matrix<R>.FromArray(n, n, uData), swaps);
    }
}
