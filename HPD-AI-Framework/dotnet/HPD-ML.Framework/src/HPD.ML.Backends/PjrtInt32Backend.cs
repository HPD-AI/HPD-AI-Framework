namespace HPD.ML.Backends.Pjrt;

public sealed class PjrtInt32Backend : Helium.Hardware.IExecutionBackend<int>, IDisposable
{
    private readonly PjrtPlugin _plugin;
    private readonly PjrtClient _client;
    private readonly PjrtExecutableCache<OperationKey> _executableCache;
    private readonly HashSet<PjrtInt32Tensor> _liveTensors = [];
    private bool _disposed;

    private PjrtInt32Backend(PjrtPlugin plugin, PjrtClient client)
    {
        _plugin = plugin;
        _client = client;
        _executableCache = new PjrtExecutableCache<OperationKey>(_client, StableHlo);
    }

    public int CachedExecutableCount
    {
        get
        {
            ThrowIfDisposed();
            return _executableCache.Count;
        }
    }

    public static bool TryCreate(out PjrtInt32Backend? backend, PjrtPluginResolverOptions? options = null)
    {
        var resolution = PjrtPluginResolver.Resolve(options ?? new PjrtPluginResolverOptions { Backend = "cpu" });
        if (!resolution.IsAvailable || resolution.LibraryPath is null)
        {
            backend = null;
            return false;
        }

        backend = Create((options ?? new PjrtPluginResolverOptions { Backend = "cpu" }) with { ExplicitPath = resolution.LibraryPath });
        return true;
    }

    public static PjrtInt32Backend Create(PjrtPluginResolverOptions? options = null)
    {
        var resolution = PjrtPluginResolver.Resolve(options ?? new PjrtPluginResolverOptions { Backend = "cpu" });
        if (!resolution.IsAvailable || resolution.LibraryPath is null)
            throw new PjrtException(resolution.ReasonUnavailable ?? "PJRT plugin is unavailable.");

        PjrtGpuRuntimeLoader.PreloadForBackend((options ?? new PjrtPluginResolverOptions { Backend = "cpu" }).Backend);
        var plugin = PjrtPlugin.Load(resolution.LibraryPath);
        try
        {
            var client = PjrtClient.Create(plugin, PjrtClientCreateOptionDefaults.WithBackendDefaults(options ?? new PjrtPluginResolverOptions { Backend = "cpu" }).ClientOptions);
            return new PjrtInt32Backend(plugin, client);
        }
        catch
        {
            plugin.Dispose();
            throw;
        }
    }

    public PjrtInt32Tensor CreateMatrix(int rows, int cols, ReadOnlySpan<int> data = default)
    {
        ThrowIfDisposed();
        PjrtBackendCore.ValidateMatrixShape(rows, cols);
        var expectedLength = checked(rows * cols);
        if (!data.IsEmpty && data.Length != expectedLength)
            throw new ArgumentException($"Data length must be {expectedLength} for a {rows}x{cols} matrix.", nameof(data));

        var buffer = data.IsEmpty
            ? _client.BufferFromHost(new int[expectedLength], [rows, cols])
            : _client.BufferFromHost(data, [rows, cols]);
        return new PjrtInt32Tensor(this, buffer, rows, cols, PjrtElementType.Int32);
    }

    Helium.Hardware.IHardwareTensor<int> Helium.Hardware.IExecutionBackend<int>.CreateMatrix(int rows, int cols, ReadOnlySpan<int> initialData)
        => CreateMatrix(rows, cols, initialData);

    public PjrtInt32Tensor MatMul(PjrtInt32Tensor a, PjrtInt32Tensor b)
    {
        ThrowIfDisposed();
        ValidateOwnedBinary(a, b, nameof(MatMul));
        if (a.Cols != b.Rows)
            throw new ArgumentException($"Cannot multiply {a.Rows}x{a.Cols} by {b.Rows}x{b.Cols}.");

        var executable = GetOrCompile(OperationKey.MatMul(a.Rows, a.Cols, b.Cols));
        return new PjrtInt32Tensor(this, executable.Execute(a.Buffer, b.Buffer), a.Rows, b.Cols, PjrtElementType.Int32);
    }

    public PjrtInt32Tensor Add(PjrtInt32Tensor a, PjrtInt32Tensor b) => Binary(a, b, OperationKind.Add, nameof(Add));
    public PjrtInt32Tensor Subtract(PjrtInt32Tensor a, PjrtInt32Tensor b) => Binary(a, b, OperationKind.Subtract, nameof(Subtract));
    public PjrtInt32Tensor Multiply(PjrtInt32Tensor a, PjrtInt32Tensor b) => Binary(a, b, OperationKind.Multiply, nameof(Multiply));

    public PjrtInt32Tensor Negate(PjrtInt32Tensor value)
    {
        ValidateUnary(value, nameof(Negate));
        var executable = GetOrCompile(OperationKey.Unary(OperationKind.Negate, value.Rows, value.Cols));
        return new PjrtInt32Tensor(this, executable.Execute(value.Buffer), value.Rows, value.Cols, PjrtElementType.Int32);
    }

    public PjrtInt32Tensor Sum(PjrtInt32Tensor value)
    {
        ValidateUnary(value, nameof(Sum));
        var executable = GetOrCompile(OperationKey.Unary(OperationKind.Sum, value.Rows, value.Cols));
        return new PjrtInt32Tensor(this, executable.Execute(value.Buffer), 1, 1, PjrtElementType.Int32);
    }

    public PjrtInt32Tensor Transpose(PjrtInt32Tensor value)
    {
        ValidateUnary(value, nameof(Transpose));
        var executable = GetOrCompile(OperationKey.Unary(OperationKind.Transpose, value.Rows, value.Cols));
        return new PjrtInt32Tensor(this, executable.Execute(value.Buffer), value.Cols, value.Rows, PjrtElementType.Int32);
    }

    public PjrtInt32Tensor Scale(PjrtInt32Tensor value, int scalar)
    {
        ValidateUnary(value, nameof(Scale));
        var executable = GetOrCompile(OperationKey.Scale(value.Rows, value.Cols, scalar));
        return new PjrtInt32Tensor(this, executable.Execute(value.Buffer), value.Rows, value.Cols, PjrtElementType.Int32);
    }

    public PjrtInt32Tensor Reshape(PjrtInt32Tensor value, int rows, int cols)
    {
        ValidateUnary(value, nameof(Reshape));
        PjrtBackendCore.ValidateMatrixShape(rows, cols);
        if (checked(rows * cols) != value.Rows * value.Cols)
            throw new ArgumentException("Target shape must preserve element count.");
        using var executable = _client.CompileMlir(StableHloProgram.Reshape(value.Rows, value.Cols, rows, cols, PjrtElementType.Int32));
        return new PjrtInt32Tensor(this, executable.Execute(value.Buffer), rows, cols, PjrtElementType.Int32);
    }

    public PjrtInt32Tensor Broadcast(PjrtInt32Tensor scalar, int rows, int cols)
    {
        ValidateUnary(scalar, nameof(Broadcast));
        PjrtBackendCore.ValidateMatrixShape(rows, cols);
        if (scalar.Rows != 1 || scalar.Cols != 1)
            throw new ArgumentException("Only 1x1 scalar broadcast is supported.", nameof(scalar));
        using var executable = _client.CompileMlir(StableHloProgram.BroadcastScalar(rows, cols, PjrtElementType.Int32));
        return new PjrtInt32Tensor(this, executable.Execute(scalar.Buffer), rows, cols, PjrtElementType.Int32);
    }

    public PjrtInt32Tensor Slice(PjrtInt32Tensor value, int startRow, int startCol, int rowCount, int colCount)
    {
        ValidateUnary(value, nameof(Slice));
        PjrtBackendCore.ValidateSlice(value.Rows, value.Cols, startRow, startCol, rowCount, colCount);
        using var executable = _client.CompileMlir(StableHloProgram.Slice(value.Rows, value.Cols, startRow, startCol, rowCount, colCount, PjrtElementType.Int32));
        return new PjrtInt32Tensor(this, executable.Execute(value.Buffer), rowCount, colCount, PjrtElementType.Int32);
    }

    public PjrtInt32Tensor Concatenate(PjrtInt32Tensor a, PjrtInt32Tensor b, int axis)
    {
        ValidateSameShapeBinary(a, b, nameof(Concatenate));
        if (axis is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(axis), "Axis must be 0 or 1.");
        using var executable = _client.CompileMlir(StableHloProgram.Concatenate(a.Rows, a.Cols, axis, PjrtElementType.Int32));
        var result = executable.Execute(a.Buffer, b.Buffer);
        return axis == 0
            ? new PjrtInt32Tensor(this, result, a.Rows + b.Rows, a.Cols, PjrtElementType.Int32)
            : new PjrtInt32Tensor(this, result, a.Rows, a.Cols + b.Cols, PjrtElementType.Int32);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var tensor in _liveTensors.ToArray())
            tensor.DisposeFromOwner();
        _liveTensors.Clear();
        _executableCache.Dispose();
        _client.Dispose();
        _plugin.Dispose();
    }

    Helium.Hardware.IHardwareTensor<int> Helium.Hardware.IExecutionBackend<int>.MatMul(Helium.Hardware.IHardwareTensor<int> left, Helium.Hardware.IHardwareTensor<int> right) => MatMul(RequirePjrtFloatTensor(left, nameof(left)), RequirePjrtFloatTensor(right, nameof(right)));
    Helium.Hardware.IHardwareTensor<int> Helium.Hardware.IExecutionBackend<int>.Add(Helium.Hardware.IHardwareTensor<int> left, Helium.Hardware.IHardwareTensor<int> right) => Add(RequirePjrtFloatTensor(left, nameof(left)), RequirePjrtFloatTensor(right, nameof(right)));
    Helium.Hardware.IHardwareTensor<int> Helium.Hardware.IExecutionBackend<int>.Subtract(Helium.Hardware.IHardwareTensor<int> left, Helium.Hardware.IHardwareTensor<int> right) => Subtract(RequirePjrtFloatTensor(left, nameof(left)), RequirePjrtFloatTensor(right, nameof(right)));
    Helium.Hardware.IHardwareTensor<int> Helium.Hardware.IExecutionBackend<int>.Multiply(Helium.Hardware.IHardwareTensor<int> left, Helium.Hardware.IHardwareTensor<int> right) => Multiply(RequirePjrtFloatTensor(left, nameof(left)), RequirePjrtFloatTensor(right, nameof(right)));
    Helium.Hardware.IHardwareTensor<int> Helium.Hardware.IExecutionBackend<int>.Negate(Helium.Hardware.IHardwareTensor<int> value) => Negate(RequirePjrtFloatTensor(value, nameof(value)));
    Helium.Hardware.IHardwareTensor<int> Helium.Hardware.IExecutionBackend<int>.Scale(Helium.Hardware.IHardwareTensor<int> value, int scalar) => Scale(RequirePjrtFloatTensor(value, nameof(value)), scalar);
    Helium.Hardware.IHardwareTensor<int> Helium.Hardware.IExecutionBackend<int>.Transpose(Helium.Hardware.IHardwareTensor<int> value) => Transpose(RequirePjrtFloatTensor(value, nameof(value)));
    int Helium.Hardware.IExecutionBackend<int>.Sum(Helium.Hardware.IHardwareTensor<int> value) { using var result = Sum(RequirePjrtFloatTensor(value, nameof(value))); return result.ToArray()[0]; }
    int Helium.Hardware.IExecutionBackend<int>.Dot(Helium.Hardware.IHardwareTensor<int> left, Helium.Hardware.IHardwareTensor<int> right) { var a = RequirePjrtFloatTensor(left, nameof(left)); var b = RequirePjrtFloatTensor(right, nameof(right)); using var product = Multiply(a, b); using var sum = Sum(product); return sum.ToArray()[0]; }
    int Helium.Hardware.IExecutionBackend<int>.Mean(Helium.Hardware.IHardwareTensor<int> value) => throw new NotSupportedException("Integer XLA mean is not supported.");
    int Helium.Hardware.IExecutionBackend<int>.Norm(Helium.Hardware.IHardwareTensor<int> value) => throw new NotSupportedException("Integer XLA norm is not supported.");
    Helium.Hardware.IHardwareTensor<int> Helium.Hardware.IExecutionBackend<int>.LinearSolve(Helium.Hardware.IHardwareTensor<int> matrix, Helium.Hardware.IHardwareTensor<int> rightHandSide) => throw new NotSupportedException("Integer XLA linear solve is not supported.");
    Helium.Hardware.IHardwareTensor<int> Helium.Hardware.IExecutionBackend<int>.MatrixInverse(Helium.Hardware.IHardwareTensor<int> value) => throw new NotSupportedException("Integer XLA matrix inverse is not supported.");

    internal PjrtBuffer BufferFromHost(ReadOnlySpan<int> data, int rows, int cols)
    {
        ThrowIfDisposed();
        return _client.BufferFromHost(data, [rows, cols]);
    }

    internal void RegisterTensor(PjrtInt32Tensor tensor) { ThrowIfDisposed(); _liveTensors.Add(tensor); }
    internal void UnregisterTensor(PjrtInt32Tensor tensor) { if (!_disposed) _liveTensors.Remove(tensor); }
    internal void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(PjrtInt32Backend)); }

    private PjrtInt32Tensor Binary(PjrtInt32Tensor a, PjrtInt32Tensor b, OperationKind kind, string name)
    {
        ValidateSameShapeBinary(a, b, name);
        var executable = GetOrCompile(OperationKey.Binary(kind, a.Rows, a.Cols));
        return new PjrtInt32Tensor(this, executable.Execute(a.Buffer, b.Buffer), a.Rows, a.Cols, PjrtElementType.Int32);
    }

    private PjrtLoadedExecutable GetOrCompile(OperationKey key) => _executableCache.GetOrCompile(key);

    private static string StableHlo(OperationKey key) => key.Kind switch
    {
        OperationKind.MatMul => StableHloProgram.MatMul(key.Rows, key.Cols, key.OutCols, PjrtElementType.Int32),
        OperationKind.Add => StableHloProgram.Binary(key.Rows, key.Cols, "stablehlo.add", PjrtElementType.Int32),
        OperationKind.Subtract => StableHloProgram.Binary(key.Rows, key.Cols, "stablehlo.subtract", PjrtElementType.Int32),
        OperationKind.Multiply => StableHloProgram.Binary(key.Rows, key.Cols, "stablehlo.multiply", PjrtElementType.Int32),
        OperationKind.Negate => StableHloProgram.Unary(key.Rows, key.Cols, "stablehlo.negate", PjrtElementType.Int32),
        OperationKind.Sum => StableHloProgram.Sum(key.Rows, key.Cols, PjrtElementType.Int32),
        OperationKind.Transpose => StableHloProgram.Transpose(key.Rows, key.Cols, PjrtElementType.Int32),
        OperationKind.Scale => StableHloProgram.Scale(key.Rows, key.Cols, key.Scalar, PjrtElementType.Int32),
        _ => throw new NotSupportedException($"Unsupported XLA operation: {key.Kind}")
    };

    private void ValidateUnary(PjrtInt32Tensor value, string operationName)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!ReferenceEquals(value.Owner, this))
            throw new ArgumentException("Tensor must be owned by this backend.", nameof(value));
        if (value.ElementType != PjrtElementType.Int32)
            throw new NotSupportedException($"Only Int32 {operationName} is supported.");
    }

    private void ValidateOwnedBinary(PjrtInt32Tensor a, PjrtInt32Tensor b, string operationName)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (!ReferenceEquals(a.Owner, this) || !ReferenceEquals(b.Owner, this))
            throw new ArgumentException("Both tensors must be owned by this backend.");
        if (a.ElementType != PjrtElementType.Int32 || b.ElementType != PjrtElementType.Int32)
            throw new NotSupportedException($"Only Int32 {operationName} is supported.");
    }

    private void ValidateSameShapeBinary(PjrtInt32Tensor a, PjrtInt32Tensor b, string operationName)
    {
        ValidateOwnedBinary(a, b, operationName);
        if (a.Rows != b.Rows || a.Cols != b.Cols)
            throw new ArgumentException($"Cannot {operationName} tensors with shapes {a.Rows}x{a.Cols} and {b.Rows}x{b.Cols}.");
    }

    private PjrtInt32Tensor RequirePjrtFloatTensor(Helium.Hardware.IHardwareTensor<int> tensor, string parameterName)
    {
        ThrowIfDisposed();
        if (tensor is not PjrtInt32Tensor xlaTensor || !ReferenceEquals(xlaTensor.Owner, this))
            throw new ArgumentException("Tensor must be owned by this XLA backend.", parameterName);
        return xlaTensor;
    }

    private readonly record struct OperationKey(OperationKind Kind, int Rows, int Cols, int OutCols, int Scalar = 0)
    {
        public static OperationKey MatMul(int rows, int cols, int outCols) => new(OperationKind.MatMul, rows, cols, outCols);
        public static OperationKey Binary(OperationKind kind, int rows, int cols) => new(kind, rows, cols, cols);
        public static OperationKey Unary(OperationKind kind, int rows, int cols) => new(kind, rows, cols, cols);
        public static OperationKey Scale(int rows, int cols, int scalar) => new(OperationKind.Scale, rows, cols, cols, scalar);
    }

    private enum OperationKind { MatMul, Add, Subtract, Multiply, Negate, Sum, Transpose, Scale }
}
