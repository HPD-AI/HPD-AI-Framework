using System.Runtime.InteropServices;
using HPD.ML.Backends.Pjrt.Interop;

namespace HPD.ML.Backends.Pjrt;

/// <summary>
/// Owns a loaded PJRT plugin library and its C API table.
/// </summary>
internal sealed unsafe class PjrtPlugin : IDisposable
{
    private readonly nint _libraryHandle;
    private bool _disposed;

    private PjrtPlugin(string libraryPath, nint libraryHandle, PjrtApi* api)
    {
        LibraryPath = libraryPath;
        _libraryHandle = libraryHandle;
        Api = api;
    }

    public string LibraryPath { get; }

    internal PjrtApi* Api { get; }

    public PjrtApiVersion ApiVersion => new(Api->PjrtApiVersion.MajorVersion, Api->PjrtApiVersion.MinorVersion);

    public nuint ApiStructSize => Api->StructSize;

    public static PjrtPlugin Load(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);

        var fullPath = Path.GetFullPath(libraryPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"PJRT plugin library does not exist: {fullPath}", fullPath);

        nint handle;
        try
        {
            handle = NativeLibrary.Load(fullPath);
        }
        catch (Exception ex)
        {
            throw new PjrtException($"Failed to load PJRT plugin library: {fullPath}", ex);
        }

        try
        {
            if (!NativeLibrary.TryGetExport(handle, "GetPjrtApi", out var symbol))
                throw new PjrtException($"PJRT plugin does not export required symbol GetPjrtApi: {fullPath}");

            var getPjrtApi = Marshal.GetDelegateForFunctionPointer<GetPjrtApiDelegate>(symbol);
            var api = getPjrtApi();
            if (api is null)
                throw new PjrtException($"GetPjrtApi returned null for PJRT plugin: {fullPath}");

            if (api->PjrtApiVersion.MajorVersion != PjrtNativeConstants.ApiMajorVersion)
            {
                throw new PjrtException(
                    $"Unsupported PJRT API major version {api->PjrtApiVersion.MajorVersion}. " +
                    $"Expected {PjrtNativeConstants.ApiMajorVersion}.");
            }

            InitializePlugin(api);

            return new PjrtPlugin(fullPath, handle, api);
        }
        catch
        {
            NativeLibrary.Free(handle);
            throw;
        }
    }

    public PjrtPluginInfo GetInfo()
        => new()
        {
            LibraryPath = LibraryPath,
            ApiVersion = ApiVersion,
            ApiStructSize = ApiStructSize
        };

    public void Dispose()
    {
        if (_disposed)
            return;

        NativeLibrary.Free(_libraryHandle);
        _disposed = true;
    }

    private static void InitializePlugin(PjrtApi* api)
    {
        var initializeFn = PjrtNative.GetFunction<PjrtPluginInitializeDelegate>(
            api->PjrtPluginInitialize,
            "PJRT_Plugin_Initialize");
        var args = new PjrtPluginInitializeArgs
        {
            StructSize = (nuint)Marshal.SizeOf<PjrtPluginInitializeArgs>(),
            ExtensionStart = null
        };

        PjrtNative.ThrowIfError(api, initializeFn(&args));
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate PjrtApi* GetPjrtApiDelegate();
}
