namespace HPD.ML.Backends.Pjrt;

public sealed class PjrtBFloat16Tensor : Helium.Hardware.IHardwareTensor<BFloat16>
{
    private PjrtBuffer _buffer;
    private bool _disposed;

    internal PjrtBFloat16Tensor(PjrtBFloat16Backend owner, PjrtBuffer buffer, int rows, int cols, PjrtElementType elementType)
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
    internal PjrtBFloat16Backend Owner { get; }

    internal PjrtBuffer Buffer
    {
        get
        {
            ThrowIfDisposed();
            return _buffer;
        }
    }

    public void CopyToHost(Span<BFloat16> destination)
    {
        ThrowIfDisposed();
        if (destination.Length != Rows * Cols)
            throw new ArgumentException($"Destination length must be {Rows * Cols} for a {Rows}x{Cols} tensor.", nameof(destination));
        _buffer.CopyTo(destination);
    }

    public void UpdateFromSpan(ReadOnlySpan<BFloat16> hostData)
    {
        ThrowIfDisposed();
        if (hostData.Length != Rows * Cols)
            throw new ArgumentException($"Source length must be {Rows * Cols} for a {Rows}x{Cols} tensor.", nameof(hostData));

        var replacement = Owner.BufferFromHost(hostData, Rows, Cols);
        _buffer.Dispose();
        _buffer = replacement;
    }

    public BFloat16[] ToArray()
    {
        ThrowIfDisposed();
        var result = new BFloat16[Rows * Cols];
        CopyToHost(result);
        return result;
    }

    public float[] ToSingleArray()
    {
        var values = ToArray();
        var result = new float[values.Length];
        for (var i = 0; i < values.Length; i++)
            result[i] = values[i].ToSingle();
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
            throw new ObjectDisposedException(nameof(PjrtBFloat16Tensor));
        Owner.ThrowIfDisposed();
    }
}
