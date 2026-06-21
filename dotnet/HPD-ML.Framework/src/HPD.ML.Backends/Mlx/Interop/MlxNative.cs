using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HPD.ML.Backends.Mlx.Interop;

internal static unsafe partial class MlxNative
{
    internal const string LibraryName = "mlx-c";

    [LibraryImport(LibraryName, EntryPoint = "mlx_set_error_handler")]
    internal static partial void SetErrorHandler(
        delegate* unmanaged[Cdecl]<byte*, void*, void> handler,
        void* data,
        delegate* unmanaged[Cdecl]<void*, void> destructor);

    [LibraryImport(LibraryName, EntryPoint = "mlx_device_new_type")]
    internal static partial MlxDeviceHandle DeviceNewType(MlxDeviceType type, int index);

    [LibraryImport(LibraryName, EntryPoint = "mlx_device_free")]
    internal static partial int DeviceFree(MlxDeviceHandle device);

    [LibraryImport(LibraryName, EntryPoint = "mlx_device_is_available")]
    internal static partial int DeviceIsAvailable(
        [MarshalAs(UnmanagedType.I1)] out bool available,
        MlxDeviceHandle device);

    [LibraryImport(LibraryName, EntryPoint = "mlx_device_count")]
    internal static partial int DeviceCount(out int count, MlxDeviceType type);

    [LibraryImport(LibraryName, EntryPoint = "mlx_default_cpu_stream_new")]
    internal static partial MlxStreamHandle DefaultCpuStreamNew();

    [LibraryImport(LibraryName, EntryPoint = "mlx_default_gpu_stream_new")]
    internal static partial MlxStreamHandle DefaultGpuStreamNew();

    [LibraryImport(LibraryName, EntryPoint = "mlx_stream_free")]
    internal static partial int StreamFree(MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_array_new_float32")]
    internal static partial MlxArrayHandle ArrayNewFloat32(float value);

    [LibraryImport(LibraryName, EntryPoint = "mlx_array_new_data")]
    internal static partial MlxArrayHandle ArrayNewData(
        void* data,
        int* shape,
        int dimensions,
        MlxDType dtype);

    [LibraryImport(LibraryName, EntryPoint = "mlx_array_free")]
    internal static partial int ArrayFree(MlxArrayHandle array);

    [LibraryImport(LibraryName, EntryPoint = "mlx_array_eval")]
    internal static partial int ArrayEval(MlxArrayHandle array);

    [LibraryImport(LibraryName, EntryPoint = "mlx_array_data_float32")]
    internal static partial float* ArrayDataFloat32(MlxArrayHandle array);

    [LibraryImport(LibraryName, EntryPoint = "mlx_array_item_float32")]
    internal static partial int ArrayItemFloat32(out float result, MlxArrayHandle array);

    [LibraryImport(LibraryName, EntryPoint = "mlx_vector_array_new_data")]
    internal static partial MlxVectorArrayHandle VectorArrayNewData(
        MlxArrayHandle* data,
        nuint size);

    [LibraryImport(LibraryName, EntryPoint = "mlx_vector_array_free")]
    internal static partial int VectorArrayFree(MlxVectorArrayHandle vector);

    [LibraryImport(LibraryName, EntryPoint = "mlx_matmul")]
    internal static partial int MatMul(
        out MlxArrayHandle result,
        MlxArrayHandle left,
        MlxArrayHandle right,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_add")]
    internal static partial int Add(
        out MlxArrayHandle result,
        MlxArrayHandle left,
        MlxArrayHandle right,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_subtract")]
    internal static partial int Subtract(
        out MlxArrayHandle result,
        MlxArrayHandle left,
        MlxArrayHandle right,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_multiply")]
    internal static partial int Multiply(
        out MlxArrayHandle result,
        MlxArrayHandle left,
        MlxArrayHandle right,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_divide")]
    internal static partial int Divide(
        out MlxArrayHandle result,
        MlxArrayHandle left,
        MlxArrayHandle right,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_maximum")]
    internal static partial int Maximum(
        out MlxArrayHandle result,
        MlxArrayHandle left,
        MlxArrayHandle right,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_negative")]
    internal static partial int Negative(
        out MlxArrayHandle result,
        MlxArrayHandle value,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_exp")]
    internal static partial int Exp(
        out MlxArrayHandle result,
        MlxArrayHandle value,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_log")]
    internal static partial int Log(
        out MlxArrayHandle result,
        MlxArrayHandle value,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_sqrt")]
    internal static partial int Sqrt(
        out MlxArrayHandle result,
        MlxArrayHandle value,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_square")]
    internal static partial int Square(
        out MlxArrayHandle result,
        MlxArrayHandle value,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_tanh")]
    internal static partial int Tanh(
        out MlxArrayHandle result,
        MlxArrayHandle value,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_sigmoid")]
    internal static partial int Sigmoid(
        out MlxArrayHandle result,
        MlxArrayHandle value,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_transpose")]
    internal static partial int Transpose(
        out MlxArrayHandle result,
        MlxArrayHandle value,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_contiguous")]
    internal static partial int Contiguous(
        out MlxArrayHandle result,
        MlxArrayHandle value,
        [MarshalAs(UnmanagedType.I1)] bool allowColumnMajor,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_reshape")]
    internal static partial int Reshape(
        out MlxArrayHandle result,
        MlxArrayHandle value,
        int* shape,
        nuint shapeLength,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_broadcast_to")]
    internal static partial int BroadcastTo(
        out MlxArrayHandle result,
        MlxArrayHandle value,
        int* shape,
        nuint shapeLength,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_slice")]
    internal static partial int Slice(
        out MlxArrayHandle result,
        MlxArrayHandle value,
        int* start,
        nuint startLength,
        int* stop,
        nuint stopLength,
        int* strides,
        nuint stridesLength,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_concatenate_axis")]
    internal static partial int ConcatenateAxis(
        out MlxArrayHandle result,
        MlxVectorArrayHandle arrays,
        int axis,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_softmax_axis")]
    internal static partial int SoftmaxAxis(
        out MlxArrayHandle result,
        MlxArrayHandle value,
        int axis,
        [MarshalAs(UnmanagedType.I1)] bool precise,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_sum")]
    internal static partial int Sum(
        out MlxArrayHandle result,
        MlxArrayHandle value,
        [MarshalAs(UnmanagedType.I1)] bool keepDimensions,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_sum_axis")]
    internal static partial int SumAxis(
        out MlxArrayHandle result,
        MlxArrayHandle value,
        int axis,
        [MarshalAs(UnmanagedType.I1)] bool keepDimensions,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_mean")]
    internal static partial int Mean(
        out MlxArrayHandle result,
        MlxArrayHandle value,
        [MarshalAs(UnmanagedType.I1)] bool keepDimensions,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_linalg_solve")]
    internal static partial int LinearSolve(
        out MlxArrayHandle result,
        MlxArrayHandle matrix,
        MlxArrayHandle rightHandSide,
        MlxStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "mlx_linalg_inv")]
    internal static partial int MatrixInverse(
        out MlxArrayHandle result,
        MlxArrayHandle matrix,
        MlxStreamHandle stream);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static void ErrorHandler(byte* message, void* data)
    {
        MlxErrorState.SetLastError(Marshal.PtrToStringUTF8((nint)message));
    }
}
