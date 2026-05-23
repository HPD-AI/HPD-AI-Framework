namespace Helium.Hardware;

public sealed class HardwareTensor<T> : IHardwareTensor<T> where T : unmanaged
{
    private readonly HardwareBuffer<T> _buffer;
    private bool _disposed;

    public HardwareTensor(int rows, int cols, ReadOnlySpan<T> initialData = default)
    {
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (cols < 0) throw new ArgumentOutOfRangeException(nameof(cols));
        var length = checked(rows * cols);
        if (!initialData.IsEmpty && initialData.Length != length)
            throw new ArgumentException("Initial data length must equal rows * cols.", nameof(initialData));

        Rows = rows;
        Cols = cols;
        _buffer = initialData.IsEmpty
            ? new HardwareBuffer<T>(length)
            : new HardwareBuffer<T>(initialData);
    }

    public int Rows { get; }
    public int Cols { get; }
    public int Length => Rows * Cols;

    internal ReadOnlySpan<T> Data
    {
        get
        {
            ThrowIfDisposed();
            return _buffer.AsSpan().ReadOnly;
        }
    }

    internal PackedSpan<T> MutableData
    {
        get
        {
            ThrowIfDisposed();
            return _buffer.AsSpan();
        }
    }

    public void CopyToHost(Span<T> hostBuffer)
    {
        if (hostBuffer.Length < Length)
            throw new ArgumentException("Destination buffer is shorter than tensor length.", nameof(hostBuffer));
        _buffer.CopyTo(hostBuffer);
    }

    public void UpdateFromSpan(ReadOnlySpan<T> hostData)
    {
        ThrowIfDisposed();
        if (hostData.Length != Length)
            throw new ArgumentException("Source data length must equal tensor length.", nameof(hostData));
        hostData.CopyTo(_buffer.AsSpan().Raw);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _buffer.Dispose();
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HardwareTensor<T>));
    }
}
