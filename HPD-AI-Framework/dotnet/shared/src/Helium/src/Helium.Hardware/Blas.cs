using System.Runtime.InteropServices;

namespace Helium.Hardware;

public enum BlasBackend
{
    Accelerate,
    Mkl,
    OpenBlas,
    ManagedFallback
}

public static unsafe class Blas
{
    private const int CblasRowMajor = 101;
    private const int CblasNoTrans = 111;

    private static readonly NativeBlas? Native = NativeBlas.TryLoad();

    public static BlasBackend ActiveBackend => Native?.Backend ?? BlasBackend.ManagedFallback;

    public static bool IsNativeAvailable => Native is not null;

    public static DoubleMatrix Multiply(DoubleMatrix a, DoubleMatrix b)
    {
        ValidateDimensions(a.Rows, a.Cols, b.Rows, b.Cols);
        return Native is { Dgemm: not null }
            ? MultiplyNative(a, b, Native.Dgemm)
            : MultiplyManaged(a, b);
    }

    public static FloatMatrix Multiply(FloatMatrix a, FloatMatrix b)
    {
        ValidateDimensions(a.Rows, a.Cols, b.Rows, b.Cols);
        return Native is { Sgemm: not null }
            ? MultiplyNative(a, b, Native.Sgemm)
            : MultiplyManaged(a, b);
    }

    public static DoubleMatrix MultiplyNativeOnly(DoubleMatrix a, DoubleMatrix b)
    {
        ValidateDimensions(a.Rows, a.Cols, b.Rows, b.Cols);
        if (Native is not { Dgemm: not null })
            throw new PlatformNotSupportedException("No native double-precision BLAS backend is available.");
        return MultiplyNative(a, b, Native.Dgemm);
    }

    public static FloatMatrix MultiplyNativeOnly(FloatMatrix a, FloatMatrix b)
    {
        ValidateDimensions(a.Rows, a.Cols, b.Rows, b.Cols);
        if (Native is not { Sgemm: not null })
            throw new PlatformNotSupportedException("No native single-precision BLAS backend is available.");
        return MultiplyNative(a, b, Native.Sgemm);
    }

    private static DoubleMatrix MultiplyNative(DoubleMatrix a, DoubleMatrix b, Dgemm dgemm)
    {
        var result = new DoubleMatrix(a.Rows, b.Cols);
        fixed (double* ap = a.Buffer.DangerousArray)
        fixed (double* bp = b.Buffer.DangerousArray)
        fixed (double* cp = result.Buffer.DangerousArray)
        {
            dgemm(
                CblasRowMajor,
                CblasNoTrans,
                CblasNoTrans,
                a.Rows,
                b.Cols,
                a.Cols,
                1.0,
                ap,
                a.Cols,
                bp,
                b.Cols,
                0.0,
                cp,
                b.Cols);
        }

        return result;
    }

    private static FloatMatrix MultiplyNative(FloatMatrix a, FloatMatrix b, Sgemm sgemm)
    {
        var result = new FloatMatrix(a.Rows, b.Cols);
        fixed (float* ap = a.Buffer.DangerousArray)
        fixed (float* bp = b.Buffer.DangerousArray)
        fixed (float* cp = result.Buffer.DangerousArray)
        {
            sgemm(
                CblasRowMajor,
                CblasNoTrans,
                CblasNoTrans,
                a.Rows,
                b.Cols,
                a.Cols,
                1.0f,
                ap,
                a.Cols,
                bp,
                b.Cols,
                0.0f,
                cp,
                b.Cols);
        }

        return result;
    }

    private static DoubleMatrix MultiplyManaged(DoubleMatrix a, DoubleMatrix b)
    {
        var result = new DoubleMatrix(a.Rows, b.Cols);
        var output = result.Buffer.AsSpan();
        var left = a.Data;
        var right = b.Data;
        for (var i = 0; i < a.Rows; i++)
        {
            for (var k = 0; k < b.Cols; k++)
            {
                var sum = 0.0;
                for (var j = 0; j < a.Cols; j++)
                    sum += left[i * a.Cols + j] * right[j * b.Cols + k];
                output[i * b.Cols + k] = sum;
            }
        }

        return result;
    }

    private static FloatMatrix MultiplyManaged(FloatMatrix a, FloatMatrix b)
    {
        var result = new FloatMatrix(a.Rows, b.Cols);
        var output = result.Buffer.AsSpan();
        var left = a.Data;
        var right = b.Data;
        for (var i = 0; i < a.Rows; i++)
        {
            for (var k = 0; k < b.Cols; k++)
            {
                var sum = 0.0f;
                for (var j = 0; j < a.Cols; j++)
                    sum += left[i * a.Cols + j] * right[j * b.Cols + k];
                output[i * b.Cols + k] = sum;
            }
        }

        return result;
    }

    private static void ValidateDimensions(int aRows, int aCols, int bRows, int bCols)
    {
        if (aCols != bRows)
            throw new ArgumentException($"Matrix dimension mismatch: ({aRows}x{aCols}) * ({bRows}x{bCols}).");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Dgemm(
        int layout,
        int transA,
        int transB,
        int m,
        int n,
        int k,
        double alpha,
        double* a,
        int lda,
        double* b,
        int ldb,
        double beta,
        double* c,
        int ldc);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Sgemm(
        int layout,
        int transA,
        int transB,
        int m,
        int n,
        int k,
        float alpha,
        float* a,
        int lda,
        float* b,
        int ldb,
        float beta,
        float* c,
        int ldc);

    private sealed class NativeBlas
    {
        private readonly nint _handle;

        private NativeBlas(nint handle, BlasBackend backend, Dgemm? dgemm, Sgemm? sgemm)
        {
            _handle = handle;
            Backend = backend;
            Dgemm = dgemm;
            Sgemm = sgemm;
        }

        public BlasBackend Backend { get; }

        public Dgemm? Dgemm { get; }

        public Sgemm? Sgemm { get; }

        public static NativeBlas? TryLoad()
        {
            foreach (var candidate in Candidates())
            {
                if (!NativeLibrary.TryLoad(candidate.Name, out var handle))
                    continue;

                var dgemm = TryGetDelegate<Dgemm>(handle, "cblas_dgemm");
                var sgemm = TryGetDelegate<Sgemm>(handle, "cblas_sgemm");
                if (dgemm is not null || sgemm is not null)
                    return new NativeBlas(handle, candidate.Backend, dgemm, sgemm);

                NativeLibrary.Free(handle);
            }

            return null;
        }

        private static T? TryGetDelegate<T>(nint handle, string symbol) where T : Delegate
        {
            return NativeLibrary.TryGetExport(handle, symbol, out var address)
                ? Marshal.GetDelegateForFunctionPointer<T>(address)
                : null;
        }

        private static IEnumerable<(string Name, BlasBackend Backend)> Candidates()
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
            {
                yield return ("/System/Library/Frameworks/Accelerate.framework/Accelerate", BlasBackend.Accelerate);
                yield return ("Accelerate", BlasBackend.Accelerate);
            }
            else if (OperatingSystem.IsWindows())
            {
                yield return ("mkl_rt", BlasBackend.Mkl);
                yield return ("libopenblas", BlasBackend.OpenBlas);
                yield return ("openblas", BlasBackend.OpenBlas);
            }
            else
            {
                yield return ("libopenblas.so", BlasBackend.OpenBlas);
                yield return ("libopenblas.so.0", BlasBackend.OpenBlas);
                yield return ("openblas", BlasBackend.OpenBlas);
                yield return ("mkl_rt", BlasBackend.Mkl);
            }
        }

        ~NativeBlas()
        {
            if (_handle != 0)
                NativeLibrary.Free(_handle);
        }
    }
}
