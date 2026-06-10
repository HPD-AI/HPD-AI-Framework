using System.Runtime.InteropServices;

namespace Rhodium.Tensor;

/// <summary>
/// Zero-cost marshaling helpers for SIMD interop.
/// Allows TensorPrimitives operations on typed wrappers.
/// Safe only for wrappers that are exactly one double wide.
/// </summary>
public static class TensorMarshal
{
    /// <summary>
    /// Cast a span of typed values to doubles for SIMD operations.
    /// </summary>
    /// <typeparam name="T">The wrapper type (must be unmanaged).</typeparam>
    /// <param name="input">The input span.</param>
    /// <returns>A span of doubles.</returns>
    public static Span<double> AsDoubles<T>(Span<T> input) where T : unmanaged =>
        MemoryMarshal.Cast<T, double>(input);

    /// <summary>
    /// Cast a read-only span of typed values to doubles for SIMD operations.
    /// </summary>
    /// <typeparam name="T">The wrapper type (must be unmanaged).</typeparam>
    /// <param name="input">The input span.</param>
    /// <returns>A read-only span of doubles.</returns>
    public static ReadOnlySpan<double> AsReadOnlyDoubles<T>(ReadOnlySpan<T> input) where T : unmanaged =>
        MemoryMarshal.Cast<T, double>(input);
}
