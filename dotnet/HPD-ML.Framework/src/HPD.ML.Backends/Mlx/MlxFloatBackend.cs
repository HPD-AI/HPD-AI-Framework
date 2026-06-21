using HPD.ML.Backends.Mlx.Interop;

namespace HPD.ML.Backends.Mlx;

public sealed class MlxFloatBackend : Helium.Hardware.IExecutionBackend<float>, IDisposable
{
    private readonly HashSet<MlxFloatTensor> _liveTensors = new();
    private readonly MlxDeviceKind _deviceKind;
    private MlxDeviceHandle _device;
    private MlxStreamHandle _stream;
    private bool _disposed;

    private MlxFloatBackend(MlxDeviceKind deviceKind, MlxDeviceHandle device, MlxStreamHandle stream)
    {
        _deviceKind = deviceKind;
        _device = device;
        _stream = stream;
    }

    public MlxDeviceKind DeviceKind => _deviceKind;

    internal MlxStreamHandle Stream => _stream;

    internal int LiveTensorCount => _liveTensors.Count;

    public static bool IsAvailable(MlxRuntimeOptions? options = null)
    {
        if (!TryCreate(out var backend, options))
            return false;

        backend.Dispose();
        return true;
    }

    public static bool TryCreate(out MlxFloatBackend? backend, MlxRuntimeOptions? options = null)
    {
        backend = null;
        var resolution = MlxRuntimeResolver.Resolve(options);
        if (!resolution.IsAvailable || resolution.LibraryPath is null)
            return false;

        try
        {
            backend = CreateFromResolvedRuntime(resolution.LibraryPath, options ?? new MlxRuntimeOptions());
            return true;
        }
        catch
        {
            backend?.Dispose();
            backend = null;
            return false;
        }
    }

    public static MlxFloatBackend Create(MlxRuntimeOptions? options = null)
    {
        options ??= new MlxRuntimeOptions();
        var resolution = MlxRuntimeResolver.Resolve(options);
        if (!resolution.IsAvailable || resolution.LibraryPath is null)
            throw new MlxException(resolution.ReasonUnavailable ?? "MLX C runtime was not found.");

        try
        {
            return CreateFromResolvedRuntime(resolution.LibraryPath, options);
        }
        catch (MlxException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MlxException($"Failed to create MLX backend from {resolution.LibraryPath}.", ex);
        }
    }

    private static MlxFloatBackend CreateFromResolvedRuntime(string libraryPath, MlxRuntimeOptions options)
    {
        MlxNativeLibrary.Configure(libraryPath);
        MlxNativeLibrary.InstallErrorHandler();

        var requested = options.Device;
        if (TryCreateForDevice(requested, out var backend))
            return backend;

        if (requested == MlxDeviceKind.Gpu && options.AllowCpuFallback && TryCreateForDevice(MlxDeviceKind.Cpu, out backend))
            return backend;

        throw new MlxException($"MLX {requested} device is not available.");
    }

    private static bool TryCreateForDevice(MlxDeviceKind deviceKind, out MlxFloatBackend backend)
    {
        backend = null!;
        var deviceType = ToNativeDeviceType(deviceKind);
        var device = MlxNative.DeviceNewType(deviceType, 0);
        if (device.IsNull)
            return false;

        try
        {
            CheckStatus(MlxNative.DeviceIsAvailable(out var available, device), "MLX device availability check failed.");
            if (!available)
            {
                CheckStatus(MlxNative.DeviceFree(device), "MLX device free failed.");
                return false;
            }

            var stream = deviceKind == MlxDeviceKind.Gpu
                ? MlxNative.DefaultGpuStreamNew()
                : MlxNative.DefaultCpuStreamNew();
            if (stream.IsNull)
                throw new MlxException($"MLX {deviceKind} stream could not be created.");

            backend = new MlxFloatBackend(deviceKind, device, stream);
            return true;
        }
        catch
        {
            CheckStatus(MlxNative.DeviceFree(device), "MLX device free failed.");
            throw;
        }
    }

    public MlxFloatTensor CreateMatrix(int rows, int cols, ReadOnlySpan<float> initialData = default)
    {
        ThrowIfDisposed();
        ValidateShape(rows, cols);

        var expected = rows * cols;
        if (initialData.IsEmpty)
            initialData = new float[expected];
        else if (initialData.Length != expected)
            throw new ArgumentException($"Initial data length must be {expected} for a {rows}x{cols} tensor.", nameof(initialData));

        return new MlxFloatTensor(this, CreateArray(initialData, rows, cols), rows, cols);
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.CreateMatrix(int rows, int cols, ReadOnlySpan<float> initialData)
        => CreateMatrix(rows, cols, initialData);

    internal unsafe MlxArrayHandle CreateArray(ReadOnlySpan<float> data, int rows, int cols)
    {
        ThrowIfDisposed();
        ValidateShape(rows, cols);
        if (data.Length != rows * cols)
            throw new ArgumentException($"Data length must be {rows * cols} for a {rows}x{cols} tensor.", nameof(data));

        var shape = stackalloc int[2] { rows, cols };
        fixed (float* dataPtr = data)
        {
            var handle = MlxNative.ArrayNewData(dataPtr, shape, 2, MlxDType.Float32);
            if (handle.IsNull)
                throw new MlxException(MlxErrorState.ConsumeLastError("MLX array creation failed."));

            return handle;
        }
    }

    public MlxFloatTensor MatMul(MlxFloatTensor left, MlxFloatTensor right)
    {
        ValidateMatMul(left, right);
        return Binary(left, right, (out MlxArrayHandle result, MlxArrayHandle a, MlxArrayHandle b, MlxStreamHandle s) => MlxNative.MatMul(out result, a, b, s), left.Rows, right.Cols, "MLX matmul failed.");
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.MatMul(Helium.Hardware.IHardwareTensor<float> left, Helium.Hardware.IHardwareTensor<float> right)
        => MatMul(RequireMlxTensor(left, nameof(left)), RequireMlxTensor(right, nameof(right)));

    public MlxFloatTensor Add(MlxFloatTensor left, MlxFloatTensor right)
    {
        ValidateSameShape(left, right, nameof(Add));
        return Binary(left, right, (out MlxArrayHandle result, MlxArrayHandle a, MlxArrayHandle b, MlxStreamHandle s) => MlxNative.Add(out result, a, b, s), left.Rows, left.Cols, "MLX add failed.");
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.Add(Helium.Hardware.IHardwareTensor<float> left, Helium.Hardware.IHardwareTensor<float> right)
        => Add(RequireMlxTensor(left, nameof(left)), RequireMlxTensor(right, nameof(right)));

    public MlxFloatTensor Subtract(MlxFloatTensor left, MlxFloatTensor right)
    {
        ValidateSameShape(left, right, nameof(Subtract));
        return Binary(left, right, (out MlxArrayHandle result, MlxArrayHandle a, MlxArrayHandle b, MlxStreamHandle s) => MlxNative.Subtract(out result, a, b, s), left.Rows, left.Cols, "MLX subtract failed.");
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.Subtract(Helium.Hardware.IHardwareTensor<float> left, Helium.Hardware.IHardwareTensor<float> right)
        => Subtract(RequireMlxTensor(left, nameof(left)), RequireMlxTensor(right, nameof(right)));

    public MlxFloatTensor Multiply(MlxFloatTensor left, MlxFloatTensor right)
    {
        ValidateSameShape(left, right, nameof(Multiply));
        return Binary(left, right, (out MlxArrayHandle result, MlxArrayHandle a, MlxArrayHandle b, MlxStreamHandle s) => MlxNative.Multiply(out result, a, b, s), left.Rows, left.Cols, "MLX multiply failed.");
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.Multiply(Helium.Hardware.IHardwareTensor<float> left, Helium.Hardware.IHardwareTensor<float> right)
        => Multiply(RequireMlxTensor(left, nameof(left)), RequireMlxTensor(right, nameof(right)));

    public MlxFloatTensor Divide(MlxFloatTensor left, MlxFloatTensor right)
    {
        ValidateSameShape(left, right, nameof(Divide));
        return Binary(left, right, (out MlxArrayHandle result, MlxArrayHandle a, MlxArrayHandle b, MlxStreamHandle s) => MlxNative.Divide(out result, a, b, s), left.Rows, left.Cols, "MLX divide failed.");
    }

    public MlxFloatTensor Maximum(MlxFloatTensor left, MlxFloatTensor right)
    {
        ValidateSameShape(left, right, nameof(Maximum));
        return Binary(left, right, (out MlxArrayHandle result, MlxArrayHandle a, MlxArrayHandle b, MlxStreamHandle s) => MlxNative.Maximum(out result, a, b, s), left.Rows, left.Cols, "MLX maximum failed.");
    }

    public MlxFloatTensor Negate(MlxFloatTensor value)
    {
        ValidateUnary(value, nameof(Negate));
        return Unary(value, (out MlxArrayHandle result, MlxArrayHandle a, MlxStreamHandle s) => MlxNative.Negative(out result, a, s), value.Rows, value.Cols, "MLX negate failed.");
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.Negate(Helium.Hardware.IHardwareTensor<float> value)
        => Negate(RequireMlxTensor(value, nameof(value)));

    public MlxFloatTensor Exp(MlxFloatTensor value)
    {
        ValidateUnary(value, nameof(Exp));
        return Unary(value, (out MlxArrayHandle result, MlxArrayHandle a, MlxStreamHandle s) => MlxNative.Exp(out result, a, s), value.Rows, value.Cols, "MLX exp failed.");
    }

    public MlxFloatTensor Log(MlxFloatTensor value)
    {
        ValidateUnary(value, nameof(Log));
        return Unary(value, (out MlxArrayHandle result, MlxArrayHandle a, MlxStreamHandle s) => MlxNative.Log(out result, a, s), value.Rows, value.Cols, "MLX log failed.");
    }

    public MlxFloatTensor Sqrt(MlxFloatTensor value)
    {
        ValidateUnary(value, nameof(Sqrt));
        return Unary(value, (out MlxArrayHandle result, MlxArrayHandle a, MlxStreamHandle s) => MlxNative.Sqrt(out result, a, s), value.Rows, value.Cols, "MLX sqrt failed.");
    }

    public MlxFloatTensor Square(MlxFloatTensor value)
    {
        ValidateUnary(value, nameof(Square));
        return Unary(value, (out MlxArrayHandle result, MlxArrayHandle a, MlxStreamHandle s) => MlxNative.Square(out result, a, s), value.Rows, value.Cols, "MLX square failed.");
    }

    public MlxFloatTensor Tanh(MlxFloatTensor value)
    {
        ValidateUnary(value, nameof(Tanh));
        return Unary(value, (out MlxArrayHandle result, MlxArrayHandle a, MlxStreamHandle s) => MlxNative.Tanh(out result, a, s), value.Rows, value.Cols, "MLX tanh failed.");
    }

    public MlxFloatTensor Sigmoid(MlxFloatTensor value)
    {
        ValidateUnary(value, nameof(Sigmoid));
        return Unary(value, (out MlxArrayHandle result, MlxArrayHandle a, MlxStreamHandle s) => MlxNative.Sigmoid(out result, a, s), value.Rows, value.Cols, "MLX sigmoid failed.");
    }

    public MlxFloatTensor Softmax(MlxFloatTensor value, int axis, bool precise = true)
    {
        ValidateUnary(value, nameof(Softmax));
        ValidateSameOwner(value, nameof(value));
        if (axis is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(axis), "Axis must be 0 or 1.");

        CheckStatus(MlxNative.SoftmaxAxis(out var result, value.Handle, axis, precise, _stream), "MLX softmax failed.");
        return WrapResult(result, value.Rows, value.Cols, "MLX softmax returned a null tensor.");
    }

    public MlxFloatTensor Scale(MlxFloatTensor value, float scalar)
    {
        ValidateUnary(value, nameof(Scale));
        var scalarHandle = MlxNative.ArrayNewFloat32(scalar);
        if (scalarHandle.IsNull)
            throw new MlxException(MlxErrorState.ConsumeLastError("MLX scalar creation failed."));

        try
        {
            CheckStatus(MlxNative.Multiply(out var result, value.Handle, scalarHandle, _stream), "MLX scale failed.");
            return WrapResult(result, value.Rows, value.Cols, "MLX scale returned a null tensor.");
        }
        finally
        {
            CheckStatus(MlxNative.ArrayFree(scalarHandle), "MLX scalar free failed.");
        }
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.Scale(Helium.Hardware.IHardwareTensor<float> value, float scalar)
        => Scale(RequireMlxTensor(value, nameof(value)), scalar);

    public MlxFloatTensor AddScalar(MlxFloatTensor value, float scalar)
    {
        ValidateUnary(value, nameof(AddScalar));
        var scalarHandle = MlxNative.ArrayNewFloat32(scalar);
        if (scalarHandle.IsNull)
            throw new MlxException(MlxErrorState.ConsumeLastError("MLX scalar creation failed."));

        try
        {
            CheckStatus(MlxNative.Add(out var result, value.Handle, scalarHandle, _stream), "MLX scalar add failed.");
            return WrapResult(result, value.Rows, value.Cols, "MLX scalar add returned a null tensor.");
        }
        finally
        {
            CheckStatus(MlxNative.ArrayFree(scalarHandle), "MLX scalar free failed.");
        }
    }

    public MlxFloatTensor DivideByScalar(MlxFloatTensor value, float scalar)
    {
        if (scalar == 0.0f)
            throw new DivideByZeroException("Cannot divide by zero.");

        return Scale(value, 1.0f / scalar);
    }

    public MlxFloatTensor Transpose(MlxFloatTensor value)
    {
        ValidateUnary(value, nameof(Transpose));
        return Unary(value, (out MlxArrayHandle result, MlxArrayHandle a, MlxStreamHandle s) => MlxNative.Transpose(out result, a, s), value.Cols, value.Rows, "MLX transpose failed.");
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.Transpose(Helium.Hardware.IHardwareTensor<float> value)
        => Transpose(RequireMlxTensor(value, nameof(value)));

    public unsafe MlxFloatTensor Reshape(MlxFloatTensor value, int rows, int cols)
    {
        ValidateUnary(value, nameof(Reshape));
        ValidateSameOwner(value, nameof(value));
        ValidateShape(rows, cols);
        if (checked(rows * cols) != value.Rows * value.Cols)
            throw new ArgumentException("Target shape must preserve element count.");

        var shape = stackalloc int[2] { rows, cols };
        CheckStatus(MlxNative.Reshape(out var result, value.Handle, shape, 2, _stream), "MLX reshape failed.");
        return WrapResult(result, rows, cols, "MLX reshape returned a null tensor.");
    }

    public unsafe MlxFloatTensor Broadcast(MlxFloatTensor scalar, int rows, int cols)
    {
        ValidateUnary(scalar, nameof(Broadcast));
        ValidateSameOwner(scalar, nameof(scalar));
        ValidateShape(rows, cols);
        if (scalar.Rows != 1 || scalar.Cols != 1)
            throw new ArgumentException("Only 1x1 scalar broadcast is supported.", nameof(scalar));

        return BroadcastTo(scalar, rows, cols);
    }

    public unsafe MlxFloatTensor BroadcastTo(MlxFloatTensor value, int rows, int cols)
    {
        ValidateUnary(value, nameof(BroadcastTo));
        ValidateSameOwner(value, nameof(value));
        ValidateShape(rows, cols);
        if (!CanBroadcast(value.Rows, value.Cols, rows, cols))
            throw new ArgumentException($"Cannot broadcast {value.Rows}x{value.Cols} to {rows}x{cols}.", nameof(value));

        var shape = stackalloc int[2] { rows, cols };
        CheckStatus(MlxNative.BroadcastTo(out var result, value.Handle, shape, 2, _stream), "MLX broadcast failed.");
        return WrapResult(result, rows, cols, "MLX broadcast returned a null tensor.");
    }

    public unsafe MlxFloatTensor Slice(MlxFloatTensor value, int startRow, int startCol, int rowCount, int colCount)
    {
        ValidateUnary(value, nameof(Slice));
        ValidateSameOwner(value, nameof(value));
        ValidateSlice(value.Rows, value.Cols, startRow, startCol, rowCount, colCount);

        var start = stackalloc int[2] { startRow, startCol };
        var stop = stackalloc int[2] { startRow + rowCount, startCol + colCount };
        var strides = stackalloc int[2] { 1, 1 };
        CheckStatus(MlxNative.Slice(out var result, value.Handle, start, 2, stop, 2, strides, 2, _stream), "MLX slice failed.");
        return WrapResult(result, rowCount, colCount, "MLX slice returned a null tensor.");
    }

    public unsafe MlxFloatTensor Concatenate(MlxFloatTensor left, MlxFloatTensor right, int axis)
    {
        ValidateConcatenate(left, right, axis);

        var handles = stackalloc MlxArrayHandle[2] { left.Handle, right.Handle };
        var arrays = MlxNative.VectorArrayNewData(handles, 2);
        if (arrays.IsNull)
            throw new MlxException(MlxErrorState.ConsumeLastError("MLX vector array creation failed."));

        try
        {
            CheckStatus(MlxNative.ConcatenateAxis(out var result, arrays, axis, _stream), "MLX concatenate failed.");
            return axis == 0
                ? WrapResult(result, left.Rows + right.Rows, left.Cols, "MLX concatenate returned a null tensor.")
                : WrapResult(result, left.Rows, left.Cols + right.Cols, "MLX concatenate returned a null tensor.");
        }
        finally
        {
            CheckStatus(MlxNative.VectorArrayFree(arrays), "MLX vector array free failed.");
        }
    }

    public MlxFloatTensor LinearSolve(MlxFloatTensor matrix, MlxFloatTensor rightHandSide)
    {
        ValidateLinearSolve(matrix, rightHandSide);
        return Binary(matrix, rightHandSide, (out MlxArrayHandle result, MlxArrayHandle a, MlxArrayHandle b, MlxStreamHandle s) => MlxNative.LinearSolve(out result, a, b, s), matrix.Cols, rightHandSide.Cols, "MLX linear solve failed.");
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.LinearSolve(Helium.Hardware.IHardwareTensor<float> matrix, Helium.Hardware.IHardwareTensor<float> rightHandSide)
        => LinearSolve(RequireMlxTensor(matrix, nameof(matrix)), RequireMlxTensor(rightHandSide, nameof(rightHandSide)));

    public MlxFloatTensor MatrixInverse(MlxFloatTensor value)
    {
        ValidateMatrixInverse(value);
        return Unary(value, (out MlxArrayHandle result, MlxArrayHandle a, MlxStreamHandle s) => MlxNative.MatrixInverse(out result, a, s), value.Rows, value.Cols, "MLX matrix inverse failed.");
    }

    Helium.Hardware.IHardwareTensor<float> Helium.Hardware.IExecutionBackend<float>.MatrixInverse(Helium.Hardware.IHardwareTensor<float> value)
        => MatrixInverse(RequireMlxTensor(value, nameof(value)));

    public float Sum(MlxFloatTensor value)
    {
        ValidateUnary(value, nameof(Sum));
        CheckStatus(MlxNative.Sum(out var result, value.Handle, false, _stream), "MLX sum failed.");
        return ReadScalarAndFree(result, "MLX sum returned a null tensor.");
    }

    float Helium.Hardware.IExecutionBackend<float>.Sum(Helium.Hardware.IHardwareTensor<float> value)
        => Sum(RequireMlxTensor(value, nameof(value)));

    public MlxFloatTensor SumAxis(MlxFloatTensor value, int axis, bool keepDimensions = true)
    {
        ValidateUnary(value, nameof(SumAxis));
        ValidateSameOwner(value, nameof(value));
        if (axis is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(axis), "Axis must be 0 or 1.");

        CheckStatus(MlxNative.SumAxis(out var result, value.Handle, axis, keepDimensions, _stream), "MLX axis sum failed.");
        if (keepDimensions)
        {
            var rows = axis == 0 ? 1 : value.Rows;
            var cols = axis == 0 ? value.Cols : 1;
            return WrapResult(result, rows, cols, "MLX axis sum returned a null tensor.");
        }

        var length = axis == 0 ? value.Cols : value.Rows;
        return WrapResult(result, 1, length, "MLX axis sum returned a null tensor.");
    }

    public float Mean(MlxFloatTensor value)
    {
        ValidateUnary(value, nameof(Mean));
        CheckStatus(MlxNative.Mean(out var result, value.Handle, false, _stream), "MLX mean failed.");
        return ReadScalarAndFree(result, "MLX mean returned a null tensor.");
    }

    float Helium.Hardware.IExecutionBackend<float>.Mean(Helium.Hardware.IHardwareTensor<float> value)
        => Mean(RequireMlxTensor(value, nameof(value)));

    public float Dot(MlxFloatTensor left, MlxFloatTensor right)
    {
        ValidateSameShape(left, right, nameof(Dot));
        using var product = Multiply(left, right);
        return Sum(product);
    }

    float Helium.Hardware.IExecutionBackend<float>.Dot(Helium.Hardware.IHardwareTensor<float> left, Helium.Hardware.IHardwareTensor<float> right)
        => Dot(RequireMlxTensor(left, nameof(left)), RequireMlxTensor(right, nameof(right)));

    public float Norm(MlxFloatTensor value)
    {
        ValidateUnary(value, nameof(Norm));
        return MathF.Sqrt(Dot(value, value));
    }

    float Helium.Hardware.IExecutionBackend<float>.Norm(Helium.Hardware.IHardwareTensor<float> value)
        => Norm(RequireMlxTensor(value, nameof(value)));

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var tensor in _liveTensors.ToArray())
            tensor.DisposeFromOwner();
        _liveTensors.Clear();

        if (!_stream.IsNull)
            CheckStatus(MlxNative.StreamFree(_stream), "MLX stream free failed.");
        if (!_device.IsNull)
            CheckStatus(MlxNative.DeviceFree(_device), "MLX device free failed.");
    }

    internal void RegisterTensor(MlxFloatTensor tensor)
    {
        _liveTensors.Add(tensor);
    }

    internal void UnregisterTensor(MlxFloatTensor tensor)
    {
        _liveTensors.Remove(tensor);
    }

    internal void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MlxFloatBackend));
    }

    internal static void CheckStatus(int status, string fallback)
    {
        if (status != 0)
            throw new MlxException(MlxErrorState.ConsumeLastError(fallback));
    }

    private delegate int BinaryOperation(out MlxArrayHandle result, MlxArrayHandle left, MlxArrayHandle right, MlxStreamHandle stream);
    private delegate int UnaryOperation(out MlxArrayHandle result, MlxArrayHandle value, MlxStreamHandle stream);

    private MlxFloatTensor Binary(MlxFloatTensor left, MlxFloatTensor right, BinaryOperation operation, int rows, int cols, string failureMessage)
    {
        CheckStatus(operation(out var result, left.Handle, right.Handle, _stream), failureMessage);
        return WrapResult(result, rows, cols, $"{failureMessage} MLX returned a null tensor.");
    }

    private MlxFloatTensor Unary(MlxFloatTensor value, UnaryOperation operation, int rows, int cols, string failureMessage)
    {
        CheckStatus(operation(out var result, value.Handle, _stream), failureMessage);
        return WrapResult(result, rows, cols, $"{failureMessage} MLX returned a null tensor.");
    }

    private MlxFloatTensor WrapResult(MlxArrayHandle result, int rows, int cols, string nullMessage)
    {
        if (result.IsNull)
            throw new MlxException(MlxErrorState.ConsumeLastError(nullMessage));

        return new MlxFloatTensor(this, result, rows, cols);
    }

    private float ReadScalarAndFree(MlxArrayHandle result, string nullMessage)
    {
        if (result.IsNull)
            throw new MlxException(MlxErrorState.ConsumeLastError(nullMessage));

        try
        {
            CheckStatus(MlxNative.ArrayEval(result), "MLX scalar evaluation failed.");
            CheckStatus(MlxNative.ArrayItemFloat32(out var value, result), "MLX scalar read failed.");
            return value;
        }
        finally
        {
            CheckStatus(MlxNative.ArrayFree(result), "MLX scalar free failed.");
        }
    }

    private static void ValidateShape(int rows, int cols)
    {
        if (rows <= 0)
            throw new ArgumentOutOfRangeException(nameof(rows), "Rows must be positive.");
        if (cols <= 0)
            throw new ArgumentOutOfRangeException(nameof(cols), "Columns must be positive.");
    }

    private static void ValidateSlice(int rows, int cols, int startRow, int startCol, int rowCount, int colCount)
    {
        ValidateShape(rowCount, colCount);
        if (startRow < 0)
            throw new ArgumentOutOfRangeException(nameof(startRow), "Start row must be non-negative.");
        if (startCol < 0)
            throw new ArgumentOutOfRangeException(nameof(startCol), "Start column must be non-negative.");
        if (startRow + rowCount > rows || startCol + colCount > cols)
            throw new ArgumentException("Slice range must be inside tensor bounds.");
    }

    private static bool CanBroadcast(int sourceRows, int sourceCols, int targetRows, int targetCols)
        => (sourceRows == targetRows || sourceRows == 1) &&
           (sourceCols == targetCols || sourceCols == 1);

    private static void ValidateUnary(MlxFloatTensor value, string operation)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Owner.ThrowIfDisposed();
        if (value.Owner._disposed)
            throw new ObjectDisposedException(nameof(MlxFloatBackend));
    }

    private void ValidateSameOwner(MlxFloatTensor tensor, string parameterName)
    {
        if (!ReferenceEquals(tensor.Owner, this))
            throw new ArgumentException("Tensor belongs to a different MLX backend.", parameterName);
    }

    private void ValidateSameShape(MlxFloatTensor left, MlxFloatTensor right, string operation)
    {
        ValidateUnary(left, operation);
        ValidateUnary(right, operation);
        ValidateSameOwner(left, nameof(left));
        ValidateSameOwner(right, nameof(right));
        if (left.Rows != right.Rows || left.Cols != right.Cols)
            throw new ArgumentException($"{operation} requires tensors with the same shape.");
    }

    private void ValidateConcatenate(MlxFloatTensor left, MlxFloatTensor right, int axis)
    {
        ValidateUnary(left, nameof(Concatenate));
        ValidateUnary(right, nameof(Concatenate));
        ValidateSameOwner(left, nameof(left));
        ValidateSameOwner(right, nameof(right));
        if (axis is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(axis), "Axis must be 0 or 1.");
        if (axis == 0 && left.Cols != right.Cols)
            throw new ArgumentException("Row concatenation requires equal column counts.");
        if (axis == 1 && left.Rows != right.Rows)
            throw new ArgumentException("Column concatenation requires equal row counts.");
    }

    private void ValidateMatMul(MlxFloatTensor left, MlxFloatTensor right)
    {
        ValidateUnary(left, nameof(MatMul));
        ValidateUnary(right, nameof(MatMul));
        ValidateSameOwner(left, nameof(left));
        ValidateSameOwner(right, nameof(right));
        if (left.Cols != right.Rows)
            throw new ArgumentException("Matrix multiplication requires left columns to equal right rows.");
    }

    private void ValidateLinearSolve(MlxFloatTensor matrix, MlxFloatTensor rightHandSide)
    {
        ValidateUnary(matrix, nameof(LinearSolve));
        ValidateUnary(rightHandSide, nameof(LinearSolve));
        ValidateSameOwner(matrix, nameof(matrix));
        ValidateSameOwner(rightHandSide, nameof(rightHandSide));
        if (matrix.Rows != matrix.Cols)
            throw new ArgumentException("Linear solve requires a square coefficient matrix.", nameof(matrix));
        if (rightHandSide.Rows != matrix.Rows)
            throw new ArgumentException("Right-hand side row count must match the coefficient matrix.", nameof(rightHandSide));
    }

    private static void ValidateMatrixInverse(MlxFloatTensor value)
    {
        ValidateUnary(value, nameof(MatrixInverse));
        if (value.Rows != value.Cols)
            throw new ArgumentException("Matrix inverse requires a square matrix.", nameof(value));
    }

    private static MlxFloatTensor RequireMlxTensor(Helium.Hardware.IHardwareTensor<float> tensor, string parameterName)
    {
        if (tensor is not MlxFloatTensor mlxTensor)
            throw new ArgumentException("Expected an MLX float tensor.", parameterName);

        return mlxTensor;
    }

    private static MlxDeviceType ToNativeDeviceType(MlxDeviceKind deviceKind)
        => deviceKind == MlxDeviceKind.Gpu ? MlxDeviceType.Gpu : MlxDeviceType.Cpu;
}
