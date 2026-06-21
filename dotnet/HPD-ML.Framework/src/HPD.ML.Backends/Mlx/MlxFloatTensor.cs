using HPD.ML.Backends.Mlx.Interop;

namespace HPD.ML.Backends.Mlx;

public sealed class MlxFloatTensor : Helium.Hardware.IHardwareTensor<float>
{
    private MlxArrayHandle _handle;
    private bool _disposed;

    internal MlxFloatTensor(MlxFloatBackend owner, MlxArrayHandle handle, int rows, int cols)
    {
        if (handle.IsNull)
            throw new ArgumentException("MLX array handle is null.", nameof(handle));

        Owner = owner;
        _handle = handle;
        Rows = rows;
        Cols = cols;
        Owner.RegisterTensor(this);
    }

    public int Rows { get; }

    public int Cols { get; }

    internal MlxFloatBackend Owner { get; }

    internal MlxArrayHandle Handle
    {
        get
        {
            ThrowIfDisposed();
            return _handle;
        }
    }

    public unsafe void CopyToHost(Span<float> hostBuffer)
    {
        ThrowIfDisposed();
        if (hostBuffer.Length != Rows * Cols)
            throw new ArgumentException($"Destination length must be {Rows * Cols} for a {Rows}x{Cols} tensor.", nameof(hostBuffer));

        MlxFloatBackend.CheckStatus(MlxNative.Contiguous(out var contiguous, _handle, false, Owner.Stream), "MLX tensor contiguous copy failed.");
        if (contiguous.IsNull)
            throw new MlxException("MLX tensor contiguous copy returned a null tensor.");

        try
        {
            MlxFloatBackend.CheckStatus(MlxNative.ArrayEval(contiguous), "MLX tensor evaluation failed.");
            var source = MlxNative.ArrayDataFloat32(contiguous);
            if (source is null)
                throw new MlxException("MLX tensor data was not available after evaluation.");

            new ReadOnlySpan<float>(source, hostBuffer.Length).CopyTo(hostBuffer);
        }
        finally
        {
            MlxFloatBackend.CheckStatus(MlxNative.ArrayFree(contiguous), "MLX temporary contiguous tensor free failed.");
        }
    }

    public unsafe void UpdateFromSpan(ReadOnlySpan<float> hostData)
    {
        ThrowIfDisposed();
        if (hostData.Length != Rows * Cols)
            throw new ArgumentException($"Source length must be {Rows * Cols} for a {Rows}x{Cols} tensor.", nameof(hostData));

        var replacement = Owner.CreateArray(hostData, Rows, Cols);
        MlxFloatBackend.CheckStatus(MlxNative.ArrayFree(_handle), "MLX tensor free failed.");
        _handle = replacement;
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
        MlxFloatBackend.CheckStatus(MlxNative.ArrayFree(_handle), "MLX tensor free failed.");
        Owner.UnregisterTensor(this);
    }

    internal void DisposeFromOwner()
    {
        if (_disposed)
            return;

        _disposed = true;
        MlxFloatBackend.CheckStatus(MlxNative.ArrayFree(_handle), "MLX tensor free failed.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MlxFloatTensor));
        Owner.ThrowIfDisposed();
    }
}
