using Helium.Algebra;

namespace Helium.Native;

/// <summary>
/// BLAS-backed fast paths for Matrix&lt;Double&gt; and Matrix&lt;Float&gt; multiplication.
/// Replaces the generic O(n³) triple-loop with cblas_dgemm / cblas_sgemm.
///
/// On macOS Apple Silicon: backed by AMX via Accelerate — 100-1000× faster than
/// the naive loop for large matrices.
///
/// The generic Matrix&lt;R&gt; type is untouched. These extensions only apply when R is
/// Double or Float, selected by the C# 14 extension block on the concrete type.
/// </summary>
public static class MatrixBlasExtensions
{
    extension(Matrix<Helium.Primitives.Double> _)
    {
        public static Matrix<Helium.Primitives.Double> operator *(Matrix<Helium.Primitives.Double> a, Matrix<Helium.Primitives.Double> b)
            => BlasInterop.Dgemm(a, b);
    }

    extension(Matrix<Helium.Primitives.Float> _)
    {
        public static Matrix<Helium.Primitives.Float> operator *(Matrix<Helium.Primitives.Float> a, Matrix<Helium.Primitives.Float> b)
            => BlasInterop.Sgemm(a, b);
    }
}
