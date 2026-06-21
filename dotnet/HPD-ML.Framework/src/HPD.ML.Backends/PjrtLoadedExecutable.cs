using System.Runtime.InteropServices;
using HPD.ML.Backends.Pjrt.Interop;

namespace HPD.ML.Backends.Pjrt;

internal sealed unsafe class PjrtLoadedExecutable : IDisposable
{
    private readonly PjrtPlugin _plugin;
    private void* _executable;
    private bool _disposed;

    internal PjrtLoadedExecutable(PjrtPlugin plugin, void* executable)
    {
        _plugin = plugin;
        _executable = executable;
    }

    public PjrtBuffer Execute(params PjrtBuffer[] arguments)
    {
        ThrowIfDisposed();

        if (arguments.Length == 0)
            throw new ArgumentException("Execution requires at least one argument.", nameof(arguments));

        var api = _plugin.Api;
        var executeFn = PjrtNative.GetFunction<PjrtLoadedExecutableExecuteDelegate>(
            api->PjrtLoadedExecutableExecute,
            "PJRT_LoadedExecutable_Execute");

        var argumentArray = stackalloc void*[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
            argumentArray[i] = arguments[i].Handle;

        var argumentLists = stackalloc void**[1];
        argumentLists[0] = argumentArray;

        var outputArray = stackalloc void*[1];
        outputArray[0] = null;
        var outputLists = stackalloc void**[1];
        outputLists[0] = outputArray;

        var completeEvents = stackalloc void*[1];
        completeEvents[0] = null;

        var options = new PjrtExecuteOptions
        {
            StructSize = (nuint)Marshal.SizeOf<PjrtExecuteOptions>(),
            ExtensionStart = null,
            SendCallbacks = null,
            RecvCallbacks = null,
            NumSendOps = 0,
            NumRecvOps = 0,
            LaunchId = 0,
            NonDonatableInputIndices = null,
            NumNonDonatableInputIndices = 0,
            Context = null,
            CallLocation = null,
            NumTasks = 0,
            TaskIds = null,
            IncarnationIds = null,
            MultiSliceConfig = null,
            UseMajorToMinorDataLayoutForCallbacks = false
        };

        var args = new PjrtLoadedExecutableExecuteArgs
        {
            StructSize = (nuint)Marshal.SizeOf<PjrtLoadedExecutableExecuteArgs>(),
            ExtensionStart = null,
            Executable = _executable,
            Options = &options,
            ArgumentLists = argumentLists,
            NumDevices = 1,
            NumArgs = (nuint)arguments.Length,
            OutputLists = outputLists,
            DeviceCompleteEvents = completeEvents,
            ExecuteDevice = null
        };

        PjrtNative.ThrowIfError(api, executeFn(&args));
        PjrtNative.AwaitAndDestroyEvent(api, completeEvents[0]);

        if (outputArray[0] is null)
            throw new PjrtException("PJRT_LoadedExecutable_Execute succeeded but returned no output buffer.");

        return new PjrtBuffer(_plugin, outputArray[0]);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        var executable = _executable;
        _executable = null;
        _disposed = true;

        if (executable is null)
            return;

        var api = _plugin.Api;
        var destroyFn = PjrtNative.GetFunction<PjrtLoadedExecutableDestroyDelegate>(
            api->PjrtLoadedExecutableDestroy,
            "PJRT_LoadedExecutable_Destroy");
        var args = new PjrtLoadedExecutableDestroyArgs
        {
            StructSize = (nuint)Marshal.SizeOf<PjrtLoadedExecutableDestroyArgs>(),
            ExtensionStart = null,
            Executable = executable
        };

        PjrtNative.ThrowIfError(api, destroyFn(&args));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PjrtLoadedExecutable));
    }
}
