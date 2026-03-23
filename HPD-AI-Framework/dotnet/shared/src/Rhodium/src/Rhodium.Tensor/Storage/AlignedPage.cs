using System.Runtime.InteropServices;

namespace Rhodium.Tensor.Storage;

/// <summary>
/// 64-byte aligned unmanaged page for tensor storage.
/// Alignment ensures optimal SIMD performance:
/// - Intel x64: Required for aligned AVX-512 loads
/// - Apple Silicon ARM64: Aligns with L1 cache line boundaries
/// </summary>
/// <typeparam name="T">The unmanaged element type.</typeparam>
internal sealed unsafe class AlignedPage<T> : IDisposable where T : unmanaged
{
    private T* _ptr;

    /// <summary>
    /// Number of elements per page.
    /// </summary>
    public const int Capacity = 1024;

    public AlignedPage()
    {
        _ptr = (T*)NativeMemory.AlignedAlloc((nuint)(Capacity * sizeof(T)), 64);
        NativeMemory.Clear(_ptr, (nuint)(Capacity * sizeof(T)));
    }

    /// <summary>
    /// Get a span over the entire page.
    /// </summary>
    public Span<T> AsSpan()
    {
        if (_ptr == null)
            throw new ObjectDisposedException(nameof(AlignedPage<T>));
        return new Span<T>(_ptr, Capacity);
    }

    public void Dispose()
    {
        if (_ptr != null)
        {
            NativeMemory.AlignedFree(_ptr);
            _ptr = null;
        }
        GC.SuppressFinalize(this);
    }
}
