using System.Numerics.Tensors;

namespace Rhodium.Tensor;

/// <summary>
/// SIMD-accelerated tensor math operations.
/// Uses System.Numerics.Tensors for hardware-accelerated computations.
/// </summary>
public static class TensorMath
{
    /// <summary>
    /// Typed overload (uses TensorMarshal to cast to doubles).
    /// </summary>
    public static void ZScore<T>(ReadOnlySpan<T> input, Span<T> output) where T : unmanaged =>
        ZScore(TensorMarshal.AsReadOnlyDoubles(input), TensorMarshal.AsDoubles(output));

    /// <summary>
    /// Calculates Z-Score (Standard Score) for an entire vector using SIMD.
    ///
    /// HARDWARE ABSTRACTION:
    /// `TensorPrimitives` is the hardware abstraction layer; the kernel adapts to the host ISA width.
    ///
    /// COMPILER MAPPING (typical):
    /// - Intel (AVX-512): Maps to 512-bit vector ops (e.g., 8 doubles per lane group)
    /// - Apple Silicon (NEON): Maps to 128-bit vector ops (e.g., 2 doubles per lane group)
    ///
    /// This abstraction incurs zero algorithmic overhead; code shape remains identical across ISAs.
    /// Z = (Value - Mean) / StdDev
    /// </summary>
    public static void ZScore(ReadOnlySpan<double> input, Span<double> output)
    {
        if (input.Length != output.Length)
            throw new ArgumentException("Length mismatch");

        // 1. Mean
        var mean = TensorPrimitives.Sum(input) / input.Length;

        // 2. Center
        TensorPrimitives.Subtract(input, mean, output);

        // 3. StdDev
        var sumSq = TensorPrimitives.Dot(output, output);
        var stdDev = Math.Sqrt(sumSq / input.Length);

        if (stdDev == 0)
        {
            output.Clear();
            return;
        }

        // 4. Normalize
        TensorPrimitives.Divide(output, stdDev, output);
    }
}
