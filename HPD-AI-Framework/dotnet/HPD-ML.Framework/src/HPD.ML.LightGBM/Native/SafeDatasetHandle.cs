namespace HPD.ML.LightGBM.Native;

/// <summary>
/// SafeHandle wrapper for a native LightGBM dataset (LGBM_DatasetHandle).
/// </summary>
internal sealed class SafeDatasetHandle : IDisposable
{
    private nint _handle;
    private bool _disposed;

    internal SafeDatasetHandle(nint handle) => _handle = handle;

    internal nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != nint.Zero)
        {
            LightGbmApi.DatasetFree(_handle);
            _handle = nint.Zero;
        }
    }
}
