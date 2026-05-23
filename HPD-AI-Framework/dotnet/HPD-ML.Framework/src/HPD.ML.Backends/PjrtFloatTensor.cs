namespace HPD.ML.Backends.Pjrt;

public sealed class PjrtFloatTensor : Helium.Hardware.IHardwareTensor<float>
{
    private PjrtBuffer _buffer;
    private bool _disposed;

    internal PjrtFloatTensor(PjrtFloatBackend owner, PjrtBuffer buffer, int rows, int cols, PjrtElementType elementType)
    {
        Owner = owner;
        _buffer = buffer;
        Rows = rows;
        Cols = cols;
        ElementType = elementType;
        Owner.RegisterTensor(this);
    }

    public int Rows { get; }

    public int Cols { get; }

    public PjrtElementType ElementType { get; }

    internal PjrtFloatBackend Owner { get; }

    internal PjrtBuffer Buffer
    {
        get
        {
            ThrowIfDisposed();
            return _buffer;
        }
    }

    public void CopyToHost(Span<float> destination)
    {
        ThrowIfDisposed();
        if (destination.Length != Rows * Cols)
        {
            throw new ArgumentException(
                $"Destination length must be {Rows * Cols} for a {Rows}x{Cols} tensor.",
                nameof(destination));
        }

        _buffer.CopyTo(destination);
    }

    public void UpdateFromSpan(ReadOnlySpan<float> hostData)
    {
        ThrowIfDisposed();
        if (hostData.Length != Rows * Cols)
        {
            throw new ArgumentException(
                $"Source length must be {Rows * Cols} for a {Rows}x{Cols} tensor.",
                nameof(hostData));
        }

        var replacement = Owner.BufferFromHost(hostData, Rows, Cols);
        _buffer.Dispose();
        _buffer = replacement;
    }

    public float[] ToArray()
    {
        ThrowIfDisposed();
        var result = new float[Rows * Cols];
        CopyToHost(result);
        return result;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _buffer.Dispose();
        Owner.UnregisterTensor(this);
    }

    internal void DisposeFromOwner()
    {
        if (_disposed)
            return;

        _disposed = true;
        _buffer.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PjrtFloatTensor));
        Owner.ThrowIfDisposed();
    }
}
