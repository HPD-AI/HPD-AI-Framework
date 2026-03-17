namespace HPD.ML.LightGBM.Native;

/// <summary>
/// SafeHandle wrapper for a native LightGBM booster (LGBM_BoosterHandle).
/// </summary>
internal sealed class SafeBoosterHandle : IDisposable
{
    private nint _handle;
    private bool _disposed;

    internal SafeBoosterHandle(nint handle) => _handle = handle;

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
            LightGbmApi.BoosterFree(_handle);
            _handle = nint.Zero;
        }
    }
}
