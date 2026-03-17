using System.Runtime.InteropServices;
using Helium.Algebra;
using HDouble = Helium.Primitives.Double;
using HFloat = Helium.Primitives.Float;

namespace Helium.Native;

/// <summary>
/// P/Invoke wrappers for platform BLAS libraries.
/// Library selection is automatic at runtime via RuntimeInformation.
///
/// macOS:   Apple Accelerate (libBLAS.dylib) — ships with every Mac, AMX-backed on Apple Silicon.
/// Windows: Intel MKL (mkl_rt.dll) — available via Intel.MKL.Redist NuGet.
/// Linux:   OpenBLAS (libopenblas.so) — available via OpenBlas.NET NuGet.
/// </summary>
internal static class BlasInterop
{
    private static readonly string _library = SelectLibrary();

    private static string SelectLibrary()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))    return "libBLAS.dylib";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "mkl_rt.dll";
        return "libopenblas.so";
    }

    // BLAS constants
    private const int CblasRowMajor = 101;
    private const int CblasNoTrans  = 111;

    // -------------------------------------------------------------------------
    // dgemm: C = alpha * A * B + beta * C  (double precision)
    // -------------------------------------------------------------------------

    [DllImport("libBLAS.dylib",  EntryPoint = "cblas_dgemm")]
    private static extern void cblas_dgemm_macos(
        int Order, int TransA, int TransB,
        int M, int N, int K,
        double alpha, double[] A, int lda,
        double[] B, int ldb,
        double beta, double[] C, int ldc);

    [DllImport("mkl_rt.dll", EntryPoint = "cblas_dgemm")]
    private static extern void cblas_dgemm_windows(
        int Order, int TransA, int TransB,
        int M, int N, int K,
        double alpha, double[] A, int lda,
        double[] B, int ldb,
        double beta, double[] C, int ldc);

    [DllImport("libopenblas.so", EntryPoint = "cblas_dgemm")]
    private static extern void cblas_dgemm_linux(
        int Order, int TransA, int TransB,
        int M, int N, int K,
        double alpha, double[] A, int lda,
        double[] B, int ldb,
        double beta, double[] C, int ldc);

    private static void Dgemm(
        int M, int N, int K,
        double alpha, double[] A, double[] B,
        double beta, double[] C)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            cblas_dgemm_macos(CblasRowMajor, CblasNoTrans, CblasNoTrans,
                M, N, K, alpha, A, K, B, N, beta, C, N);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cblas_dgemm_windows(CblasRowMajor, CblasNoTrans, CblasNoTrans,
                M, N, K, alpha, A, K, B, N, beta, C, N);
        else
            cblas_dgemm_linux(CblasRowMajor, CblasNoTrans, CblasNoTrans,
                M, N, K, alpha, A, K, B, N, beta, C, N);
    }

    // -------------------------------------------------------------------------
    // sgemm: C = alpha * A * B + beta * C  (single precision)
    // -------------------------------------------------------------------------

    [DllImport("libBLAS.dylib",  EntryPoint = "cblas_sgemm")]
    private static extern void cblas_sgemm_macos(
        int Order, int TransA, int TransB,
        int M, int N, int K,
        float alpha, float[] A, int lda,
        float[] B, int ldb,
        float beta, float[] C, int ldc);

    [DllImport("mkl_rt.dll", EntryPoint = "cblas_sgemm")]
    private static extern void cblas_sgemm_windows(
        int Order, int TransA, int TransB,
        int M, int N, int K,
        float alpha, float[] A, int lda,
        float[] B, int ldb,
        float beta, float[] C, int ldc);

    [DllImport("libopenblas.so", EntryPoint = "cblas_sgemm")]
    private static extern void cblas_sgemm_linux(
        int Order, int TransA, int TransB,
        int M, int N, int K,
        float alpha, float[] A, int lda,
        float[] B, int ldb,
        float beta, float[] C, int ldc);

    private static void Sgemm(
        int M, int N, int K,
        float alpha, float[] A, float[] B,
        float beta, float[] C)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            cblas_sgemm_macos(CblasRowMajor, CblasNoTrans, CblasNoTrans,
                M, N, K, alpha, A, K, B, N, beta, C, N);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cblas_sgemm_windows(CblasRowMajor, CblasNoTrans, CblasNoTrans,
                M, N, K, alpha, A, K, B, N, beta, C, N);
        else
            cblas_sgemm_linux(CblasRowMajor, CblasNoTrans, CblasNoTrans,
                M, N, K, alpha, A, K, B, N, beta, C, N);
    }

    // -------------------------------------------------------------------------
    // Public matrix multiply entry points
    // -------------------------------------------------------------------------

    /// <summary>
    /// BLAS dgemm: C = A * B for Matrix&lt;Double&gt;.
    /// A is m×k, B is k×n, result is m×n.
    /// </summary>
    public static Matrix<HDouble> Dgemm(Matrix<HDouble> a, Matrix<HDouble> b)
    {
        int m = a.Rows, k = a.Cols, n = b.Cols;
        if (k != b.Rows)
            throw new ArgumentException($"Matrix dimension mismatch: ({m}×{k}) * ({b.Rows}×{n}).");

        var aFlat = ExtractDoubles(a);
        var bFlat = ExtractDoubles(b);
        var cFlat = new double[m * n];

        Dgemm(m, n, k, 1.0, aFlat, bFlat, 0.0, cFlat);

        return BuildDouble(cFlat, m, n);
    }

    /// <summary>
    /// BLAS sgemm: C = A * B for Matrix&lt;Float&gt;.
    /// A is m×k, B is k×n, result is m×n.
    /// </summary>
    public static Matrix<HFloat> Sgemm(Matrix<HFloat> a, Matrix<HFloat> b)
    {
        int m = a.Rows, k = a.Cols, n = b.Cols;
        if (k != b.Rows)
            throw new ArgumentException($"Matrix dimension mismatch: ({m}×{k}) * ({b.Rows}×{n}).");

        var aFlat = ExtractFloats(a);
        var bFlat = ExtractFloats(b);
        var cFlat = new float[m * n];

        Sgemm(m, n, k, 1.0f, aFlat, bFlat, 0.0f, cFlat);

        return BuildFloat(cFlat, m, n);
    }

    // -------------------------------------------------------------------------
    // Helpers: extract contiguous arrays from Matrix<Double/Float>
    // -------------------------------------------------------------------------

    private static double[] ExtractDoubles(Matrix<HDouble> m)
    {
        var flat = new double[m.Rows * m.Cols];
        for (int i = 0; i < m.Rows; i++)
            for (int j = 0; j < m.Cols; j++)
                flat[i * m.Cols + j] = (double)m[i, j];
        return flat;
    }

    private static Matrix<HDouble> BuildDouble(double[] flat, int rows, int cols)
    {
        var entries = new HDouble[flat.Length];
        for (int k = 0; k < flat.Length; k++)
            entries[k] = new HDouble(flat[k]);
        return Matrix<HDouble>.FromArray(rows, cols, entries);
    }

    private static float[] ExtractFloats(Matrix<HFloat> m)
    {
        var flat = new float[m.Rows * m.Cols];
        for (int i = 0; i < m.Rows; i++)
            for (int j = 0; j < m.Cols; j++)
                flat[i * m.Cols + j] = (float)m[i, j];
        return flat;
    }

    private static Matrix<HFloat> BuildFloat(float[] flat, int rows, int cols)
    {
        var entries = new HFloat[flat.Length];
        for (int k = 0; k < flat.Length; k++)
            entries[k] = new HFloat(flat[k]);
        return Matrix<HFloat>.FromArray(rows, cols, entries);
    }
}
