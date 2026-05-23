using System.Runtime.InteropServices;

namespace HPD.ML.Backends.Pjrt.Interop;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void* PjrtPluginInitializeDelegate(PjrtPluginInitializeArgs* args);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void PjrtErrorDestroyDelegate(PjrtErrorDestroyArgs* args);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void PjrtErrorMessageDelegate(PjrtErrorMessageArgs* args);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void* PjrtClientCreateDelegate(PjrtClientCreateArgs* args);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void* PjrtClientDestroyDelegate(PjrtClientDestroyArgs* args);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void* PjrtClientPlatformNameDelegate(PjrtClientPlatformNameArgs* args);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void* PjrtClientPlatformVersionDelegate(PjrtClientPlatformVersionArgs* args);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void* PjrtClientDevicesDelegate(PjrtClientDevicesArgs* args);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void* PjrtEventDestroyDelegate(PjrtEventDestroyArgs* args);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void* PjrtEventAwaitDelegate(PjrtEventAwaitArgs* args);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void* PjrtClientAddressableDevicesDelegate(PjrtClientAddressableDevicesArgs* args);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void* PjrtClientCompileDelegate(PjrtClientCompileArgs* args);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void* PjrtClientBufferFromHostBufferDelegate(PjrtClientBufferFromHostBufferArgs* args);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void* PjrtLoadedExecutableDestroyDelegate(PjrtLoadedExecutableDestroyArgs* args);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void* PjrtLoadedExecutableExecuteDelegate(PjrtLoadedExecutableExecuteArgs* args);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void* PjrtBufferDestroyDelegate(PjrtBufferDestroyArgs* args);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate void* PjrtBufferToHostBufferDelegate(PjrtBufferToHostBufferArgs* args);
