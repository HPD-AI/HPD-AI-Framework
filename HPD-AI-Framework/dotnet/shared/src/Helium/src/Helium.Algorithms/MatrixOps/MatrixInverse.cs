using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algorithms;

/// <summary>
/// Matrix inverse via Gauss-Jordan elimination over a field.
/// </summary>
public static class MatrixInverse
{
    /// <summary>
    /// Computes the inverse of a square matrix. Returns null if singular.
    /// </summary>
    public static Matrix<R>? Compute<R>(Matrix<R> m)
        where R : IField<R>
    {
        int n = m.Rows;
        if (n != m.Cols)
            throw new ArgumentException("Matrix must be square.");

        // Augmented matrix [A | I].
        var aug = new R[n, 2 * n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
                aug[i, j] = m[i, j];
            for (int j = 0; j < n; j++)
                aug[i, n + j] = i == j ? R.MultiplicativeIdentity : R.AdditiveIdentity;
        }

        // Forward elimination.
        for (int k = 0; k < n; k++)
        {
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
                for (int j = 0; j < 2 * n; j++)
                    (aug[k, j], aug[pivotRow, j]) = (aug[pivotRow, j], aug[k, j]);
            }

            var pivotInv = R.Invert(aug[k, k]);
            for (int j = 0; j < 2 * n; j++)
                aug[k, j] = aug[k, j] * pivotInv;

            for (int i = 0; i < n; i++)
            {
                if (i == k) continue;
                var factor = aug[i, k];
                for (int j = 0; j < 2 * n; j++)
                    aug[i, j] = aug[i, j] - factor * aug[k, j];
            }
        }

        // Extract the right half.
        var data = new R[n * n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                data[i * n + j] = aug[i, n + j];

        return Matrix<R>.FromArray(n, n, data);
    }
}
