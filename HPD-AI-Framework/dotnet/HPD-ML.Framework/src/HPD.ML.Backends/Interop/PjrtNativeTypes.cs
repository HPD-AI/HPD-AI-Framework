using System.Runtime.InteropServices;

namespace HPD.ML.Backends.Pjrt.Interop;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtExtensionBase
{
    public nuint StructSize;
    public int Type;
    public PjrtExtensionBase* Next;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtApiVersionNative
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public int MajorVersion;
    public int MinorVersion;
}

// Prefix of PJRT_Api through the first milestone functions. The full table is
// intentionally not mirrored yet; we add fields only as implementation milestones
// require them, preserving the exact C order from pjrt_c_api.h.
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtApi
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public PjrtApiVersionNative PjrtApiVersion;
    public nint PjrtErrorDestroy;
    public nint PjrtErrorMessage;
    public nint PjrtErrorGetCode;
    public nint PjrtPluginInitialize;
    public nint PjrtPluginAttributes;
    public nint PjrtEventDestroy;
    public nint PjrtEventIsReady;
    public nint PjrtEventError;
    public nint PjrtEventAwait;
    public nint PjrtEventOnReady;
    public nint PjrtClientCreate;
    public nint PjrtClientDestroy;
    public nint PjrtClientPlatformName;
    public nint PjrtClientProcessIndex;
    public nint PjrtClientPlatformVersion;
    public nint PjrtClientDevices;
    public nint PjrtClientAddressableDevices;
    public nint PjrtClientLookupDevice;
    public nint PjrtClientLookupAddressableDevice;
    public nint PjrtClientAddressableMemories;
    public nint PjrtClientCompile;
    public nint PjrtClientDefaultDeviceAssignment;
    public nint PjrtClientBufferFromHostBuffer;
    public nint PjrtDeviceDescriptionId;
    public nint PjrtDeviceDescriptionProcessIndex;
    public nint PjrtDeviceDescriptionAttributes;
    public nint PjrtDeviceDescriptionKind;
    public nint PjrtDeviceDescriptionDebugString;
    public nint PjrtDeviceDescriptionToString;
    public nint PjrtDeviceGetDescription;
    public nint PjrtDeviceIsAddressable;
    public nint PjrtDeviceLocalHardwareId;
    public nint PjrtDeviceAddressableMemories;
    public nint PjrtDeviceDefaultMemory;
    public nint PjrtDeviceMemoryStats;
    public nint PjrtMemoryId;
    public nint PjrtMemoryKind;
    public nint PjrtMemoryDebugString;
    public nint PjrtMemoryToString;
    public nint PjrtMemoryAddressableByDevices;
    public nint PjrtExecutableDestroy;
    public nint PjrtExecutableName;
    public nint PjrtExecutableNumReplicas;
    public nint PjrtExecutableNumPartitions;
    public nint PjrtExecutableNumOutputs;
    public nint PjrtExecutableSizeOfGeneratedCodeInBytes;
    public nint PjrtExecutableGetCostAnalysis;
    public nint PjrtExecutableOutputMemoryKinds;
    public nint PjrtExecutableOptimizedProgram;
    public nint PjrtExecutableSerialize;
    public nint PjrtLoadedExecutableDestroy;
    public nint PjrtLoadedExecutableGetExecutable;
    public nint PjrtLoadedExecutableAddressableDevices;
    public nint PjrtLoadedExecutableDelete;
    public nint PjrtLoadedExecutableIsDeleted;
    public nint PjrtLoadedExecutableExecute;
    public nint PjrtExecutableDeserializeAndLoad;
    public nint PjrtLoadedExecutableFingerprint;
    public nint PjrtBufferDestroy;
    public nint PjrtBufferElementType;
    public nint PjrtBufferDimensions;
    public nint PjrtBufferUnpaddedDimensions;
    public nint PjrtBufferDynamicDimensionIndices;
    public nint PjrtBufferGetMemoryLayout;
    public nint PjrtBufferOnDeviceSizeInBytes;
    public nint PjrtBufferDevice;
    public nint PjrtBufferMemory;
    public nint PjrtBufferDelete;
    public nint PjrtBufferIsDeleted;
    public nint PjrtBufferCopyToDevice;
    public nint PjrtBufferToHostBuffer;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtPluginInitializeArgs
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtErrorDestroyArgs
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public void* Error;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtErrorMessageArgs
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public void* Error;
    public byte* Message;
    public nuint MessageSize;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtClientCreateArgs
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public void* CreateOptions;
    public nuint NumOptions;
    public nint KeyValueGetCallback;
    public void* KeyValueGetUserArg;
    public nint KeyValuePutCallback;
    public void* KeyValuePutUserArg;
    public void* Client;
    public nint KeyValueTryGetCallback;
    public void* KeyValueTryGetUserArg;
}

internal enum PjrtNamedValueType
{
    String = 0,
    Int64 = 1,
    Int64List = 2,
    Float = 3,
    Bool = 4
}

[StructLayout(LayoutKind.Explicit)]
internal unsafe struct PjrtNamedValueUnion
{
    [FieldOffset(0)]
    public byte* StringValue;

    [FieldOffset(0)]
    public long Int64Value;

    [FieldOffset(0)]
    public long* Int64ArrayValue;

    [FieldOffset(0)]
    public float FloatValue;

    [FieldOffset(0)]
    public byte BoolValue;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtNamedValue
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public byte* Name;
    public nuint NameSize;
    public PjrtNamedValueType Type;
    public PjrtNamedValueUnion Value;
    public nuint ValueSize;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtClientDestroyArgs
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public void* Client;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtClientPlatformNameArgs
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public void* Client;
    public byte* PlatformName;
    public nuint PlatformNameSize;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtClientPlatformVersionArgs
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public void* Client;
    public byte* PlatformVersion;
    public nuint PlatformVersionSize;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtClientDevicesArgs
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public void* Client;
    public void* Devices;
    public nuint NumDevices;
}

internal enum PjrtBufferType
{
    Invalid = 0,
    Pred = 1,
    S8 = 2,
    S16 = 3,
    S32 = 4,
    S64 = 5,
    U8 = 6,
    U16 = 7,
    U32 = 8,
    U64 = 9,
    F16 = 10,
    F32 = 11,
    F64 = 12,
    Bf16 = 13
}

internal enum PjrtHostBufferSemantics
{
    ImmutableOnlyDuringCall = 0,
    ImmutableUntilTransferCompletes = 1,
    ImmutableZeroCopy = 2,
    MutableZeroCopy = 3
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtEventDestroyArgs
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public void* Event;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtEventAwaitArgs
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public void* Event;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtClientAddressableDevicesArgs
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public void* Client;
    public void* AddressableDevices;
    public nuint NumAddressableDevices;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtProgram
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public byte* Code;
    public nuint CodeSize;
    public byte* Format;
    public nuint FormatSize;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtClientCompileArgs
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public void* Client;
    public PjrtProgram* Program;
    public byte* CompileOptions;
    public nuint CompileOptionsSize;
    public void* Executable;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtClientBufferFromHostBufferArgs
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public void* Client;
    public void* Data;
    public PjrtBufferType Type;
    public long* Dims;
    public nuint NumDims;
    public long* ByteStrides;
    public nuint NumByteStrides;
    public PjrtHostBufferSemantics HostBufferSemantics;
    public void* Device;
    public void* Memory;
    public void* DeviceLayout;
    public void* DoneWithHostBuffer;
    public void* Buffer;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtLoadedExecutableDestroyArgs
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public void* Executable;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtExecuteOptions
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public void* SendCallbacks;
    public void* RecvCallbacks;
    public nuint NumSendOps;
    public nuint NumRecvOps;
    public int LaunchId;
    public long* NonDonatableInputIndices;
    public nuint NumNonDonatableInputIndices;
    public void* Context;
    public byte* CallLocation;
    public nuint NumTasks;
    public int* TaskIds;
    public long* IncarnationIds;
    public void* MultiSliceConfig;
    [MarshalAs(UnmanagedType.I1)]
    public bool UseMajorToMinorDataLayoutForCallbacks;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtLoadedExecutableExecuteArgs
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public void* Executable;
    public PjrtExecuteOptions* Options;
    public void* ArgumentLists;
    public nuint NumDevices;
    public nuint NumArgs;
    public void* OutputLists;
    public void* DeviceCompleteEvents;
    public void* ExecuteDevice;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtBufferDestroyArgs
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public void* Buffer;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PjrtBufferToHostBufferArgs
{
    public nuint StructSize;
    public PjrtExtensionBase* ExtensionStart;
    public void* Src;
    public void* HostLayout;
    public void* Dst;
    public nuint DstSize;
    public void* Event;
}
