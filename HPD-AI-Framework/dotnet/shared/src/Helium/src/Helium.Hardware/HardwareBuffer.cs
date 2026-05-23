namespace Helium.Hardware;

/// <summary>
/// Owns pinned contiguous memory for hardware-friendly computation.
/// </summary>
public sealed class HardwareBuffer<T> : IDisposable where T : unmanaged
{
    private readonly T[] _data;
    private bool _disposed;

    public HardwareBuffer(int length)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        _data = GC.AllocateArray<T>(length, pinned: true);
    }

    public HardwareBuffer(ReadOnlySpan<T> initialData)
        : this(initialData.Length)
    {
        initialData.CopyTo(_data);
    }

    public int Length
    {
        get
        {
            ThrowIfDisposed();
            return _data.Length;
        }
    }

    public PackedSpan<T> AsSpan()
    {
        ThrowIfDisposed();
        return new PackedSpan<T>(_data);
    }

    public PackedSpan<T> AsSpan(int start, int length)
    {
        ThrowIfDisposed();
        return new PackedSpan<T>(_data.AsSpan(start, length));
    }

    public void CopyTo(Span<T> destination)
    {
        ThrowIfDisposed();
        _data.AsSpan().CopyTo(destination);
    }

    internal T[] DangerousArray
    {
        get
        {
            ThrowIfDisposed();
            return _data;
        }
    }

    public void Dispose() => _disposed = true;

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HardwareBuffer<T>));
    }
}
