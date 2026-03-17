using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algorithms;

/// <summary>
/// Matrix determinant via Bareiss algorithm (fraction-free) for commutative rings,
/// and via LU decomposition for fields.
/// </summary>
public static class Determinant
{
    /// <summary>
    /// Computes the determinant of a square matrix over a commutative ring
    /// using the Bareiss algorithm (fraction-free Gaussian elimination).
    /// Requires IEuclideanDomain for exact division.
    /// </summary>
    public static R Compute<R>(Matrix<R> m)
        where R : ICommRing<R>, IEuclideanDomain<R>
    {
        int n = m.Rows;
        if (n != m.Cols)
            throw new ArgumentException("Matrix must be square.");

        if (n == 0) return R.MultiplicativeIdentity;
        if (n == 1) return m[0, 0];
        if (n == 2)
            return m[0, 0] * m[1, 1] - m[0, 1] * m[1, 0];

        // Bareiss algorithm: fraction-free forward elimination.
        // Work on a mutable copy.
        var a = new R[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                a[i, j] = m[i, j];

        var sign = R.MultiplicativeIdentity;
        var prev = R.MultiplicativeIdentity;

        for (int k = 0; k < n - 1; k++)
        {
            // Partial pivoting: find nonzero pivot in column k.
            int pivotRow = -1;
            for (int i = k; i < n; i++)
            {
                if (!a[i, k].Equals(R.AdditiveIdentity))
                {
                    pivotRow = i;
                    break;
                }
            }

            if (pivotRow == -1)
                return R.AdditiveIdentity; // Singular.

            if (pivotRow != k)
            {
                // Swap rows.
                for (int j = 0; j < n; j++)
                    (a[k, j], a[pivotRow, j]) = (a[pivotRow, j], a[k, j]);
                sign = -sign;
            }

            var pivot = a[k, k];
            for (int i = k + 1; i < n; i++)
            {
                for (int j = k + 1; j < n; j++)
                {
                    var numerator = a[k, k] * a[i, j] - a[i, k] * a[k, j];
                    var (q, _) = R.DivMod(numerator, prev);
                    a[i, j] = q;
                }
                a[i, k] = R.AdditiveIdentity;
            }
            prev = pivot;
        }

        return sign * a[n - 1, n - 1];
    }

    /// <summary>
    /// Computes the determinant of a square matrix over a field using LU decomposition.
    /// </summary>
    public static R ComputeOverField<R>(Matrix<R> m)
        where R : IField<R>
    {
        var result = LUDecomposition.Decompose(m);
        if (result is null)
            return R.AdditiveIdentity;

        var (_, u, swaps) = result.Value;
        var det = R.MultiplicativeIdentity;
        int n = m.Rows;
        for (int i = 0; i < n; i++)
            det = det * u[i, i];

        // Each row swap flips the sign.
        if (swaps % 2 != 0)
            det = -det;

        return det;
    }
}
