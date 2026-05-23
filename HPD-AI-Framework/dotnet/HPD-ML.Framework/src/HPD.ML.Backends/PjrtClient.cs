using System.Runtime.InteropServices;
using System.Text;
using HPD.ML.Backends.Pjrt.Interop;

namespace HPD.ML.Backends.Pjrt;

/// <summary>
/// Minimal PJRT client wrapper used by the XLA probe path.
/// </summary>
internal sealed unsafe class PjrtClient : IDisposable
{
    private readonly PjrtPlugin _plugin;
    private void* _client;
    private bool _disposed;

    private PjrtClient(PjrtPlugin plugin, void* client)
    {
        _plugin = plugin;
        _client = client;
    }

    public static PjrtClient Create(PjrtPlugin plugin, PjrtClientCreateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        var api = plugin.Api;
        var createFn = PjrtNative.GetFunction<PjrtClientCreateDelegate>(api->PjrtClientCreate, "PJRT_Client_Create");
        using var nativeOptions = PjrtNativeCreateOptions.From(options);
        var args = new PjrtClientCreateArgs
        {
            StructSize = (nuint)Marshal.SizeOf<PjrtClientCreateArgs>(),
            ExtensionStart = null,
            CreateOptions = nativeOptions.Values,
            NumOptions = nativeOptions.Count,
            KeyValueGetCallback = 0,
            KeyValueGetUserArg = null,
            KeyValuePutCallback = 0,
            KeyValuePutUserArg = null,
            Client = null,
            KeyValueTryGetCallback = 0,
            KeyValueTryGetUserArg = null
        };

        PjrtNative.ThrowIfError(api, createFn(&args));
        if (args.Client is null)
            throw new PjrtException("PJRT_Client_Create succeeded but returned a null client.");

        return new PjrtClient(plugin, args.Client);
    }

    public PjrtClientInfo GetInfo()
        => new()
        {
            PlatformName = GetPlatformName(),
            PlatformVersion = GetPlatformVersion(),
            DeviceCount = GetDeviceCount()
        };

    public string GetPlatformName()
    {
        ThrowIfDisposed();

        var api = _plugin.Api;
        var platformNameFn = PjrtNative.GetFunction<PjrtClientPlatformNameDelegate>(
            api->PjrtClientPlatformName,
            "PJRT_Client_PlatformName");
        var args = new PjrtClientPlatformNameArgs
        {
            StructSize = (nuint)Marshal.SizeOf<PjrtClientPlatformNameArgs>(),
            ExtensionStart = null,
            Client = _client,
            PlatformName = null,
            PlatformNameSize = 0
        };

        PjrtNative.ThrowIfError(api, platformNameFn(&args));
        return PjrtNative.ReadUtf8(args.PlatformName, args.PlatformNameSize);
    }

    public string GetPlatformVersion()
    {
        ThrowIfDisposed();

        var api = _plugin.Api;
        var platformVersionFn = PjrtNative.GetFunction<PjrtClientPlatformVersionDelegate>(
            api->PjrtClientPlatformVersion,
            "PJRT_Client_PlatformVersion");
        var args = new PjrtClientPlatformVersionArgs
        {
            StructSize = (nuint)Marshal.SizeOf<PjrtClientPlatformVersionArgs>(),
            ExtensionStart = null,
            Client = _client,
            PlatformVersion = null,
            PlatformVersionSize = 0
        };

        PjrtNative.ThrowIfError(api, platformVersionFn(&args));
        return PjrtNative.ReadUtf8(args.PlatformVersion, args.PlatformVersionSize);
    }

    public int GetDeviceCount()
    {
        ThrowIfDisposed();

        var api = _plugin.Api;
        var devicesFn = PjrtNative.GetFunction<PjrtClientDevicesDelegate>(api->PjrtClientDevices, "PJRT_Client_Devices");
        var args = new PjrtClientDevicesArgs
        {
            StructSize = (nuint)Marshal.SizeOf<PjrtClientDevicesArgs>(),
            ExtensionStart = null,
            Client = _client,
            Devices = null,
            NumDevices = 0
        };

        PjrtNative.ThrowIfError(api, devicesFn(&args));
        if (args.NumDevices > int.MaxValue)
            throw new PjrtException($"PJRT reported too many devices to represent: {args.NumDevices}.");

        return checked((int)args.NumDevices);
    }

    public PjrtBuffer BufferFromHost(ReadOnlySpan<float> data, ReadOnlySpan<long> dimensions)
        => BufferFromHost(data, dimensions, PjrtBufferType.F32, sizeof(float));

    public PjrtBuffer BufferFromHost(ReadOnlySpan<double> data, ReadOnlySpan<long> dimensions)
        => BufferFromHost(data, dimensions, PjrtBufferType.F64, sizeof(double));

    public PjrtBuffer BufferFromHost(ReadOnlySpan<int> data, ReadOnlySpan<long> dimensions)
        => BufferFromHost(data, dimensions, PjrtBufferType.S32, sizeof(int));

    public PjrtBuffer BufferFromHost(ReadOnlySpan<long> data, ReadOnlySpan<long> dimensions)
        => BufferFromHost(data, dimensions, PjrtBufferType.S64, sizeof(long));

    public PjrtBuffer BufferFromHost(ReadOnlySpan<BFloat16> data, ReadOnlySpan<long> dimensions)
        => BufferFromHost(data, dimensions, PjrtBufferType.Bf16, sizeof(ushort));

    private PjrtBuffer BufferFromHost<T>(
        ReadOnlySpan<T> data,
        ReadOnlySpan<long> dimensions,
        PjrtBufferType bufferType,
        int elementSize)
        where T : unmanaged
    {
        ThrowIfDisposed();

        if (data.IsEmpty)
            throw new ArgumentException("Host buffer must not be empty.", nameof(data));
        if (dimensions.IsEmpty)
            throw new ArgumentException("Dimensions must not be empty.", nameof(dimensions));

        long elementCount = 1;
        foreach (var dimension in dimensions)
        {
            if (dimension <= 0)
                throw new ArgumentOutOfRangeException(nameof(dimensions), "Dimensions must be positive.");
            elementCount = checked(elementCount * dimension);
        }

        if (elementCount != data.Length)
        {
            throw new ArgumentException(
                $"Dimensions describe {elementCount} elements but host buffer contains {data.Length}.",
                nameof(dimensions));
        }

        var api = _plugin.Api;
        var bufferFromHostFn = PjrtNative.GetFunction<PjrtClientBufferFromHostBufferDelegate>(
            api->PjrtClientBufferFromHostBuffer,
            "PJRT_Client_BufferFromHostBuffer");

        var device = GetFirstAddressableDevice();
        fixed (T* dataPtr = data)
        fixed (long* dimsPtr = dimensions)
        {
            var args = new PjrtClientBufferFromHostBufferArgs
            {
                StructSize = (nuint)Marshal.SizeOf<PjrtClientBufferFromHostBufferArgs>(),
                ExtensionStart = null,
                Client = _client,
                Data = dataPtr,
                Type = bufferType,
                Dims = dimsPtr,
                NumDims = (nuint)dimensions.Length,
                ByteStrides = null,
                NumByteStrides = 0,
                HostBufferSemantics = PjrtHostBufferSemantics.ImmutableOnlyDuringCall,
                Device = device,
                Memory = null,
                DeviceLayout = null,
                DoneWithHostBuffer = null,
                Buffer = null
            };

            PjrtNative.ThrowIfError(api, bufferFromHostFn(&args));
            PjrtNative.AwaitAndDestroyEvent(api, args.DoneWithHostBuffer);

            if (args.Buffer is null)
                throw new PjrtException("PJRT_Client_BufferFromHostBuffer succeeded but returned a null buffer.");

            return new PjrtBuffer(_plugin, args.Buffer);
        }
    }

    public PjrtLoadedExecutable CompileMlir(string mlir)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(mlir);

        var api = _plugin.Api;
        var compileFn = PjrtNative.GetFunction<PjrtClientCompileDelegate>(api->PjrtClientCompile, "PJRT_Client_Compile");
        var code = Encoding.UTF8.GetBytes(mlir);
        var format = Encoding.UTF8.GetBytes("mlir");
        var compileOptions = SingleDeviceCompileOptionsProto();

        fixed (byte* codePtr = code)
        fixed (byte* formatPtr = format)
        fixed (byte* compileOptionsPtr = compileOptions)
        {
            var program = new PjrtProgram
            {
                StructSize = (nuint)Marshal.SizeOf<PjrtProgram>(),
                ExtensionStart = null,
                Code = codePtr,
                CodeSize = (nuint)code.Length,
                Format = formatPtr,
                FormatSize = (nuint)format.Length
            };

            var args = new PjrtClientCompileArgs
            {
                StructSize = (nuint)Marshal.SizeOf<PjrtClientCompileArgs>(),
                ExtensionStart = null,
                Client = _client,
                Program = &program,
                CompileOptions = compileOptionsPtr,
                CompileOptionsSize = (nuint)compileOptions.Length,
                Executable = null
            };

            PjrtNative.ThrowIfError(api, compileFn(&args));
            if (args.Executable is null)
                throw new PjrtException("PJRT_Client_Compile succeeded but returned a null executable.");

            return new PjrtLoadedExecutable(_plugin, args.Executable);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        var client = _client;
        _client = null;
        _disposed = true;

        if (client is null)
            return;

        var api = _plugin.Api;
        var destroyFn = PjrtNative.GetFunction<PjrtClientDestroyDelegate>(api->PjrtClientDestroy, "PJRT_Client_Destroy");
        var args = new PjrtClientDestroyArgs
        {
            StructSize = (nuint)Marshal.SizeOf<PjrtClientDestroyArgs>(),
            ExtensionStart = null,
            Client = client
        };

        PjrtNative.ThrowIfError(api, destroyFn(&args));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PjrtClient));
    }

    private void* GetFirstAddressableDevice()
    {
        var api = _plugin.Api;
        var devicesFn = PjrtNative.GetFunction<PjrtClientAddressableDevicesDelegate>(
            api->PjrtClientAddressableDevices,
            "PJRT_Client_AddressableDevices");
        var args = new PjrtClientAddressableDevicesArgs
        {
            StructSize = (nuint)Marshal.SizeOf<PjrtClientAddressableDevicesArgs>(),
            ExtensionStart = null,
            Client = _client,
            AddressableDevices = null,
            NumAddressableDevices = 0
        };

        PjrtNative.ThrowIfError(api, devicesFn(&args));
        if (args.NumAddressableDevices == 0 || args.AddressableDevices is null)
            throw new PjrtException("PJRT client reported no addressable devices.");

        return ((void**)args.AddressableDevices)[0];
    }

    private static byte[] SingleDeviceCompileOptionsProto()
    {
        // xla.CompileOptionsProto {
        //   executable_build_options {
        //     num_replicas: 1
        //     num_partitions: 1
        //     device_assignment {
        //       replica_count: 1
        //       computation_count: 1
        //       computation_devices { replica_device_ids: 0 }
        //     }
        //   }
        // }
        return
        [
            0x1A, 0x0F,
            0x20, 0x01,
            0x28, 0x01,
            0x4A, 0x09,
            0x08, 0x01,
            0x10, 0x01,
            0x1A, 0x03,
            0x0A, 0x01, 0x00
        ];
    }
}
