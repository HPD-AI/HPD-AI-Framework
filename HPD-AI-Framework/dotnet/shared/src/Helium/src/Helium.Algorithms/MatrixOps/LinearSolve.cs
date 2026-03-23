using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algorithms;

/// <summary>
/// Solves linear systems Ax = b over a field using Gaussian elimination with partial pivoting.
/// </summary>
public static class LinearSolve
{
    /// <summary>
    /// Solves Ax = b. Returns the solution vector, or null if the system is singular.
    /// </summary>
    public static Vector<R>? Solve<R>(Matrix<R> a, Vector<R> b)
        where R : IField<R>
    {
        int n = a.Rows;
        if (n != a.Cols)
            throw new ArgumentException("Matrix must be square.");
        if (b.Length != n)
            throw new ArgumentException("Vector dimension must match matrix rows.");

        // Augmented matrix [A | b].
        var aug = new R[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
                aug[i, j] = a[i, j];
            aug[i, n] = b[i];
        }

        // Forward elimination with partial pivoting.
        for (int k = 0; k < n; k++)
        {
            // Find pivot.
            int pivotRow = -1;
            for (int i = k; i < n; i++)
            {
                if (!aug[i, k].Equals(R.AdditiveIdentity))
                {
                    pivotRow = i;
                    break;
                }
            }

            if (pivotRow == -1)
                return null; // Singular.

            if (pivotRow != k)
            {
                for (int j = k; j <= n; j++)
                    (aug[k, j], aug[pivotRow, j]) = (aug[pivotRow, j], aug[k, j]);
            }

            var pivotInv = R.Invert(aug[k, k]);
            for (int j = k; j <= n; j++)
                aug[k, j] = aug[k, j] * pivotInv;

            for (int i = k + 1; i < n; i++)
            {
                var factor = aug[i, k];
                for (int j = k; j <= n; j++)
                    aug[i, j] = aug[i, j] - factor * aug[k, j];
            }
        }

        // Back substitution.
        var x = new R[n];
        for (int i = n - 1; i >= 0; i--)
        {
            var sum = aug[i, n];
            for (int j = i + 1; j < n; j++)
                sum = sum - aug[i, j] * x[j];
            x[i] = sum;
        }

        return Vector<R>.FromArray(x);
    }
}
