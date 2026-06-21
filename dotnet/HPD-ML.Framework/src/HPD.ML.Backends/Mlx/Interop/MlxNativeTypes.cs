using System.Runtime.InteropServices;

namespace HPD.ML.Backends.Mlx.Interop;

internal enum MlxDType
{
    Bool,
    UInt8,
    UInt16,
    UInt32,
    UInt64,
    Int8,
    Int16,
    Int32,
    Int64,
    Float16,
    Float32,
    Float64,
    BFloat16,
    Complex64
}

internal enum MlxDeviceType
{
    Cpu,
    Gpu
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct MlxArrayHandle
{
    public readonly nint Context;
    public bool IsNull => Context == 0;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct MlxDeviceHandle
{
    public readonly nint Context;
    public bool IsNull => Context == 0;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct MlxStreamHandle
{
    public readonly nint Context;
    public bool IsNull => Context == 0;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct MlxVectorArrayHandle
{
    public readonly nint Context;
    public bool IsNull => Context == 0;
}
