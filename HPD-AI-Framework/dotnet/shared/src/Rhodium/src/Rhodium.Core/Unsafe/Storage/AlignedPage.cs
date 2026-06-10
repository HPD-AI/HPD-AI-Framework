using System.Runtime.InteropServices;

namespace Rhodium.Unsafe.Storage;

[HighPerformanceKernel]
public sealed unsafe class AlignedPage<T> : IDisposable where T : unmanaged
{
    private readonly GlobalMemoryTracker? _tracker;
    private T* _ptr;

    public const int Capacity = 1024;
    public static int ByteSize => Capacity * sizeof(T);

    public AlignedPage(GlobalMemoryTracker? tracker = null)
    {
        _tracker = tracker;
        _ptr = (T*)NativeMemory.AlignedAlloc((nuint)ByteSize, 64);
        NativeMemory.Clear(_ptr, (nuint)ByteSize);
        _tracker?.TrackAllocation(ByteSize);
    }

    public Span<T> AsSpan()
    {
        if (_ptr == null)
            throw new ObjectDisposedException(nameof(AlignedPage<T>));
        return new Span<T>(_ptr, Capacity);
    }

    public void Clear() => AsSpan().Clear();

    public void Dispose()
    {
        if (_ptr != null)
        {
            NativeMemory.AlignedFree(_ptr);
            _ptr = null;
            _tracker?.TrackDeallocation(ByteSize);
        }

        GC.SuppressFinalize(this);
    }
}
