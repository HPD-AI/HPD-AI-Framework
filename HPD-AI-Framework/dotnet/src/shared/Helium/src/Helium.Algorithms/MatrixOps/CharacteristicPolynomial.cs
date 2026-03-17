using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algorithms;

/// <summary>
/// Computes the characteristic polynomial det(xI - A) of a square matrix
/// using the Faddeev-LeVerrier algorithm.
/// </summary>
public static class CharacteristicPolynomial
{
    /// <summary>
    /// Computes the characteristic polynomial of an n x n matrix.
    /// Result is monic of degree n: x^n + c_{n-1}*x^{n-1} + ... + c_0.
    /// The constant term c_0 = (-1)^n * det(A).
    /// </summary>
    public static Polynomial<R> Compute<R>(Matrix<R> m)
        where R : ICommRing<R>, IEuclideanDomain<R>
    {
        int n = m.Rows;
        if (n != m.Cols)
            throw new ArgumentException("Matrix must be square.");

        if (n == 0)
            return Polynomial<R>.One;

        // Faddeev-LeVerrier: compute coefficients c_{n-1}, ..., c_0.
        // M_k = A * M_{k-1} + c_{n-k} * I, with M_0 = 0 and c_n = 1.
        // c_{n-k} = -Trace(A * M_{k-1}) / k

        // Since we're over a commutative ring (not necessarily a field), we need
        // exact division by small integers. We use the recurrence:
        // k * c_{n-k} = -Trace(A * M_{k-1})
        // This requires division by k, which works for Integers/Rationals.

        var coeffs = new R[n + 1];
        coeffs[n] = R.MultiplicativeIdentity; // Monic.

        var mk = Matrix<R>.Zero(n, n); // M_0 = 0

        for (int k = 1; k <= n; k++)
        {
            // M_k = A * M_{k-1} + c_{n-k+1} * I
            mk = m * mk + coeffs[n - k + 1] * Matrix<R>.Identity(n);

            // Trace(A * M_k) but we need Trace(A * M_{k-1} + c * I * A) ...
            // Actually: k * c_{n-k} = -Trace(A * M_{k-1} + c_{n-k+1} * A)
            // Simpler: use the fact that Trace(mk * A) / (-k) but let's just
            // compute directly.

            // Trace(m * mk)
            var amk = m * mk;
            var trace = R.AdditiveIdentity;
            for (int i = 0; i < n; i++)
                trace = trace + amk[i, i];

            // c_{n-k} = -trace / k
            var (ck, _) = R.DivMod(-trace, R.FromInt(k));
            coeffs[n - k] = ck;
        }

        return Polynomial<R>.FromCoeffs(coeffs);
    }
}
