using System.Runtime.InteropServices;
using HPD.ML.Backends.Pjrt.Interop;

namespace HPD.ML.Backends.Pjrt;

internal sealed unsafe class PjrtBuffer : IDisposable
{
    private readonly PjrtPlugin _plugin;
    private void* _buffer;
    private bool _disposed;

    internal PjrtBuffer(PjrtPlugin plugin, void* buffer)
    {
        _plugin = plugin;
        _buffer = buffer;
    }

    internal void* Handle
    {
        get
        {
            ThrowIfDisposed();
            return _buffer;
        }
    }

    public void CopyTo(Span<float> destination)
    {
        ThrowIfDisposed();

        if (destination.IsEmpty)
            throw new ArgumentException("Destination must not be empty.", nameof(destination));

        var api = _plugin.Api;
        var toHostFn = PjrtNative.GetFunction<PjrtBufferToHostBufferDelegate>(
            api->PjrtBufferToHostBuffer,
            "PJRT_Buffer_ToHostBuffer");

        fixed (float* dstPtr = destination)
        {
            var args = new PjrtBufferToHostBufferArgs
            {
                StructSize = (nuint)Marshal.SizeOf<PjrtBufferToHostBufferArgs>(),
                ExtensionStart = null,
                Src = _buffer,
                HostLayout = null,
                Dst = dstPtr,
                DstSize = (nuint)(destination.Length * sizeof(float)),
                Event = null
            };

            PjrtNative.ThrowIfError(api, toHostFn(&args));
            PjrtNative.AwaitAndDestroyEvent(api, args.Event);
        }
    }

    public void CopyTo(Span<double> destination)
    {
        ThrowIfDisposed();

        if (destination.IsEmpty)
            throw new ArgumentException("Destination must not be empty.", nameof(destination));

        var api = _plugin.Api;
        var toHostFn = PjrtNative.GetFunction<PjrtBufferToHostBufferDelegate>(
            api->PjrtBufferToHostBuffer,
            "PJRT_Buffer_ToHostBuffer");

        fixed (double* dstPtr = destination)
        {
            var args = new PjrtBufferToHostBufferArgs
            {
                StructSize = (nuint)Marshal.SizeOf<PjrtBufferToHostBufferArgs>(),
                ExtensionStart = null,
                Src = _buffer,
                HostLayout = null,
                Dst = dstPtr,
                DstSize = (nuint)(destination.Length * sizeof(double)),
                Event = null
            };

            PjrtNative.ThrowIfError(api, toHostFn(&args));
            PjrtNative.AwaitAndDestroyEvent(api, args.Event);
        }
    }

    public void CopyTo(Span<int> destination)
    {
        ThrowIfDisposed();
        CopyToUnmanaged(destination, sizeof(int));
    }

    public void CopyTo(Span<long> destination)
    {
        ThrowIfDisposed();
        CopyToUnmanaged(destination, sizeof(long));
    }

    public void CopyTo(Span<BFloat16> destination)
    {
        ThrowIfDisposed();
        CopyToUnmanaged(destination, sizeof(ushort));
    }

    private void CopyToUnmanaged<T>(Span<T> destination, int elementSize)
        where T : unmanaged
    {
        if (destination.IsEmpty)
            throw new ArgumentException("Destination must not be empty.", nameof(destination));

        var api = _plugin.Api;
        var toHostFn = PjrtNative.GetFunction<PjrtBufferToHostBufferDelegate>(
            api->PjrtBufferToHostBuffer,
            "PJRT_Buffer_ToHostBuffer");

        fixed (T* dstPtr = destination)
        {
            var args = new PjrtBufferToHostBufferArgs
            {
                StructSize = (nuint)Marshal.SizeOf<PjrtBufferToHostBufferArgs>(),
                ExtensionStart = null,
                Src = _buffer,
                HostLayout = null,
                Dst = dstPtr,
                DstSize = (nuint)(destination.Length * elementSize),
                Event = null
            };

            PjrtNative.ThrowIfError(api, toHostFn(&args));
            PjrtNative.AwaitAndDestroyEvent(api, args.Event);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        var buffer = _buffer;
        _buffer = null;
        _disposed = true;

        if (buffer is null)
            return;

        var api = _plugin.Api;
        var destroyFn = PjrtNative.GetFunction<PjrtBufferDestroyDelegate>(api->PjrtBufferDestroy, "PJRT_Buffer_Destroy");
        var args = new PjrtBufferDestroyArgs
        {
            StructSize = (nuint)Marshal.SizeOf<PjrtBufferDestroyArgs>(),
            ExtensionStart = null,
            Buffer = buffer
        };

        PjrtNative.ThrowIfError(api, destroyFn(&args));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PjrtBuffer));
    }
}
