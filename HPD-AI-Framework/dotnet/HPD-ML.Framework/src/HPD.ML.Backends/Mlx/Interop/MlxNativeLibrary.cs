using System.Reflection;
using System.Runtime.InteropServices;

namespace HPD.ML.Backends.Mlx.Interop;

internal static class MlxNativeLibrary
{
    private static readonly object Gate = new();
    private static string? _libraryPath;
    private static nint _handle;
    private static bool _resolverRegistered;
    private static bool _errorHandlerInstalled;

    public static void Configure(string libraryPath)
    {
        if (string.IsNullOrWhiteSpace(libraryPath))
            throw new ArgumentException("MLX native library path is required.", nameof(libraryPath));

        var fullPath = Path.GetFullPath(libraryPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("MLX native library was not found.", fullPath);

        lock (Gate)
        {
            if (_libraryPath is not null && !StringComparer.Ordinal.Equals(_libraryPath, fullPath))
                throw new InvalidOperationException($"MLX native library is already configured: {_libraryPath}");

            _libraryPath = fullPath;

            if (!_resolverRegistered)
            {
                NativeLibrary.SetDllImportResolver(typeof(MlxNativeLibrary).Assembly, Resolve);
                _resolverRegistered = true;
            }
        }
    }

    public static unsafe void InstallErrorHandler()
    {
        lock (Gate)
        {
            if (_errorHandlerInstalled)
                return;

            MlxNative.SetErrorHandler(&MlxNative.ErrorHandler, null, null);
            _errorHandlerInstalled = true;
        }
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!StringComparer.Ordinal.Equals(libraryName, MlxNative.LibraryName))
            return 0;

        lock (Gate)
        {
            if (_handle != 0)
                return _handle;

            if (_libraryPath is null)
                return 0;

            _handle = NativeLibrary.Load(_libraryPath);
            return _handle;
        }
    }
}

