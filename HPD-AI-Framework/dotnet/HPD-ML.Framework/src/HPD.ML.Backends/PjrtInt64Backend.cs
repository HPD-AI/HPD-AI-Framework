namespace HPD.ML.Backends.Pjrt;

public sealed class PjrtInt64Backend : Helium.Hardware.IExecutionBackend<long>, IDisposable
{
    private readonly PjrtPlugin _plugin;
    private readonly PjrtClient _client;
    private readonly PjrtExecutableCache<OperationKey> _executableCache;
    private readonly HashSet<PjrtInt64Tensor> _liveTensors = [];
    private bool _disposed;

    private PjrtInt64Backend(PjrtPlugin plugin, PjrtClient client)
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

    public static bool TryCreate(out PjrtInt64Backend? backend, PjrtPluginResolverOptions? options = null)
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

    public static PjrtInt64Backend Create(PjrtPluginResolverOptions? options = null)
    {
        var resolution = PjrtPluginResolver.Resolve(options ?? new PjrtPluginResolverOptions { Backend = "cpu" });
        if (!resolution.IsAvailable || resolution.LibraryPath is null)
            throw new PjrtException(resolution.ReasonUnavailable ?? "PJRT plugin is unavailable.");

        PjrtGpuRuntimeLoader.PreloadForBackend((options ?? new PjrtPluginResolverOptions { Backend = "cpu" }).Backend);
        var plugin = PjrtPlugin.Load(resolution.LibraryPath);
        try
        {
            var client = PjrtClient.Create(plugin, PjrtClientCreateOptionDefaults.WithBackendDefaults(options ?? new PjrtPluginResolverOptions { Backend = "cpu" }).ClientOptions);
            return new PjrtInt64Backend(plugin, client);
        }
        catch
        {
            plugin.Dispose();
            throw;
        }
    }

    public PjrtInt64Tensor CreateMatrix(int rows, int cols, ReadOnlySpan<long> data = default)
    {
        ThrowIfDisposed();
        PjrtBackendCore.ValidateMatrixShape(rows, cols);
        var expectedLength = checked(rows * cols);
        if (!data.IsEmpty && data.Length != expectedLength)
            throw new ArgumentException($"Data length must be {expectedLength} for a {rows}x{cols} matrix.", nameof(data));

        var buffer = data.IsEmpty
            ? _client.BufferFromHost(new long[expectedLength], [rows, cols])
            : _client.BufferFromHost(data, [rows, cols]);
        return new PjrtInt64Tensor(this, buffer, rows, cols, PjrtElementType.Int64);
    }

    Helium.Hardware.IHardwareTensor<long> Helium.Hardware.IExecutionBackend<long>.CreateMatrix(int rows, int cols, ReadOnlySpan<long> initialData)
        => CreateMatrix(rows, cols, initialData);

    public PjrtInt64Tensor MatMul(PjrtInt64Tensor a, PjrtInt64Tensor b)
    {
        ThrowIfDisposed();
        ValidateOwnedBinary(a, b, nameof(MatMul));
        if (a.Cols != b.Rows)
            throw new ArgumentException($"Cannot multiply {a.Rows}x{a.Cols} by {b.Rows}x{b.Cols}.");

        var executable = GetOrCompile(OperationKey.MatMul(a.Rows, a.Cols, b.Cols));
        return new PjrtInt64Tensor(this, executable.Execute(a.Buffer, b.Buffer), a.Rows, b.Cols, PjrtElementType.Int64);
    }

    public PjrtInt64Tensor Add(PjrtInt64Tensor a, PjrtInt64Tensor b) => Binary(a, b, OperationKind.Add, nameof(Add));
    public PjrtInt64Tensor Subtract(PjrtInt64Tensor a, PjrtInt64Tensor b) => Binary(a, b, OperationKind.Subtract, nameof(Subtract));
    public PjrtInt64Tensor Multiply(PjrtInt64Tensor a, PjrtInt64Tensor b) => Binary(a, b, OperationKind.Multiply, nameof(Multiply));

    public PjrtInt64Tensor Negate(PjrtInt64Tensor value)
    {
        ValidateUnary(value, nameof(Negate));
        var executable = GetOrCompile(OperationKey.Unary(OperationKind.Negate, value.Rows, value.Cols));
        return new PjrtInt64Tensor(this, executable.Execute(value.Buffer), value.Rows, value.Cols, PjrtElementType.Int64);
    }

    public PjrtInt64Tensor Sum(PjrtInt64Tensor value)
    {
        ValidateUnary(value, nameof(Sum));
        var executable = GetOrCompile(OperationKey.Unary(OperationKind.Sum, value.Rows, value.Cols));
        return new PjrtInt64Tensor(this, executable.Execute(value.Buffer), 1, 1, PjrtElementType.Int64);
    }

    public PjrtInt64Tensor Transpose(PjrtInt64Tensor value)
    {
        ValidateUnary(value, nameof(Transpose));
        var executable = GetOrCompile(OperationKey.Unary(OperationKind.Transpose, value.Rows, value.Cols));
        return new PjrtInt64Tensor(this, executable.Execute(value.Buffer), value.Cols, value.Rows, PjrtElementType.Int64);
    }

    public PjrtInt64Tensor Scale(PjrtInt64Tensor value, long scalar)
    {
        ValidateUnary(value, nameof(Scale));
        var executable = GetOrCompile(OperationKey.Scale(value.Rows, value.Cols, scalar));
        return new PjrtInt64Tensor(this, executable.Execute(value.Buffer), value.Rows, value.Cols, PjrtElementType.Int64);
    }

    public PjrtInt64Tensor Reshape(PjrtInt64Tensor value, int rows, int cols)
    {
        ValidateUnary(value, nameof(Reshape));
        PjrtBackendCore.ValidateMatrixShape(rows, cols);
        if (checked(rows * cols) != value.Rows * value.Cols)
            throw new ArgumentException("Target shape must preserve element count.");
        using var executable = _client.CompileMlir(StableHloProgram.Reshape(value.Rows, value.Cols, rows, cols, PjrtElementType.Int64));
        return new PjrtInt64Tensor(this, executable.Execute(value.Buffer), rows, cols, PjrtElementType.Int64);
    }

    public PjrtInt64Tensor Broadcast(PjrtInt64Tensor scalar, int rows, int cols)
    {
        ValidateUnary(scalar, nameof(Broadcast));
        PjrtBackendCore.ValidateMatrixShape(rows, cols);
        if (scalar.Rows != 1 || scalar.Cols != 1)
            throw new ArgumentException("Only 1x1 scalar broadcast is supported.", nameof(scalar));
        using var executable = _client.CompileMlir(StableHloProgram.BroadcastScalar(rows, cols, PjrtElementType.Int64));
        return new PjrtInt64Tensor(this, executable.Execute(scalar.Buffer), rows, cols, PjrtElementType.Int64);
    }

    public PjrtInt64Tensor Slice(PjrtInt64Tensor value, int startRow, int startCol, int rowCount, int colCount)
    {
        ValidateUnary(value, nameof(Slice));
        PjrtBackendCore.ValidateSlice(value.Rows, value.Cols, startRow, startCol, rowCount, colCount);
        using var executable = _client.CompileMlir(StableHloProgram.Slice(value.Rows, value.Cols, startRow, startCol, rowCount, colCount, PjrtElementType.Int64));
        return new PjrtInt64Tensor(this, executable.Execute(value.Buffer), rowCount, colCount, PjrtElementType.Int64);
    }

    public PjrtInt64Tensor Concatenate(PjrtInt64Tensor a, PjrtInt64Tensor b, int axis)
    {
        ValidateSameShapeBinary(a, b, nameof(Concatenate));
        if (axis is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(axis), "Axis must be 0 or 1.");
        using var executable = _client.CompileMlir(StableHloProgram.Concatenate(a.Rows, a.Cols, axis, PjrtElementType.Int64));
        var result = executable.Execute(a.Buffer, b.Buffer);
        return axis == 0
            ? new PjrtInt64Tensor(this, result, a.Rows + b.Rows, a.Cols, PjrtElementType.Int64)
            : new PjrtInt64Tensor(this, result, a.Rows, a.Cols + b.Cols, PjrtElementType.Int64);
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

    Helium.Hardware.IHardwareTensor<long> Helium.Hardware.IExecutionBackend<long>.MatMul(Helium.Hardware.IHardwareTensor<long> left, Helium.Hardware.IHardwareTensor<long> right) => MatMul(RequirePjrtFloatTensor(left, nameof(left)), RequirePjrtFloatTensor(right, nameof(right)));
    Helium.Hardware.IHardwareTensor<long> Helium.Hardware.IExecutionBackend<long>.Add(Helium.Hardware.IHardwareTensor<long> left, Helium.Hardware.IHardwareTensor<long> right) => Add(RequirePjrtFloatTensor(left, nameof(left)), RequirePjrtFloatTensor(right, nameof(right)));
    Helium.Hardware.IHardwareTensor<long> Helium.Hardware.IExecutionBackend<long>.Subtract(Helium.Hardware.IHardwareTensor<long> left, Helium.Hardware.IHardwareTensor<long> right) => Subtract(RequirePjrtFloatTensor(left, nameof(left)), RequirePjrtFloatTensor(right, nameof(right)));
    Helium.Hardware.IHardwareTensor<long> Helium.Hardware.IExecutionBackend<long>.Multiply(Helium.Hardware.IHardwareTensor<long> left, Helium.Hardware.IHardwareTensor<long> right) => Multiply(RequirePjrtFloatTensor(left, nameof(left)), RequirePjrtFloatTensor(right, nameof(right)));
    Helium.Hardware.IHardwareTensor<long> Helium.Hardware.IExecutionBackend<long>.Negate(Helium.Hardware.IHardwareTensor<long> value) => Negate(RequirePjrtFloatTensor(value, nameof(value)));
    Helium.Hardware.IHardwareTensor<long> Helium.Hardware.IExecutionBackend<long>.Scale(Helium.Hardware.IHardwareTensor<long> value, long scalar) => Scale(RequirePjrtFloatTensor(value, nameof(value)), scalar);
    Helium.Hardware.IHardwareTensor<long> Helium.Hardware.IExecutionBackend<long>.Transpose(Helium.Hardware.IHardwareTensor<long> value) => Transpose(RequirePjrtFloatTensor(value, nameof(value)));
    long Helium.Hardware.IExecutionBackend<long>.Sum(Helium.Hardware.IHardwareTensor<long> value) { using var result = Sum(RequirePjrtFloatTensor(value, nameof(value))); return result.ToArray()[0]; }
    long Helium.Hardware.IExecutionBackend<long>.Dot(Helium.Hardware.IHardwareTensor<long> left, Helium.Hardware.IHardwareTensor<long> right) { var a = RequirePjrtFloatTensor(left, nameof(left)); var b = RequirePjrtFloatTensor(right, nameof(right)); using var product = Multiply(a, b); using var sum = Sum(product); return sum.ToArray()[0]; }
    long Helium.Hardware.IExecutionBackend<long>.Mean(Helium.Hardware.IHardwareTensor<long> value) => throw new NotSupportedException("Integer XLA mean is not supported.");
    long Helium.Hardware.IExecutionBackend<long>.Norm(Helium.Hardware.IHardwareTensor<long> value) => throw new NotSupportedException("Integer XLA norm is not supported.");
    Helium.Hardware.IHardwareTensor<long> Helium.Hardware.IExecutionBackend<long>.LinearSolve(Helium.Hardware.IHardwareTensor<long> matrix, Helium.Hardware.IHardwareTensor<long> rightHandSide) => throw new NotSupportedException("Integer XLA linear solve is not supported.");
    Helium.Hardware.IHardwareTensor<long> Helium.Hardware.IExecutionBackend<long>.MatrixInverse(Helium.Hardware.IHardwareTensor<long> value) => throw new NotSupportedException("Integer XLA matrix inverse is not supported.");

    internal PjrtBuffer BufferFromHost(ReadOnlySpan<long> data, int rows, int cols)
    {
        ThrowIfDisposed();
        return _client.BufferFromHost(data, [rows, cols]);
    }

    internal void RegisterTensor(PjrtInt64Tensor tensor) { ThrowIfDisposed(); _liveTensors.Add(tensor); }
    internal void UnregisterTensor(PjrtInt64Tensor tensor) { if (!_disposed) _liveTensors.Remove(tensor); }
    internal void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(PjrtInt64Backend)); }

    private PjrtInt64Tensor Binary(PjrtInt64Tensor a, PjrtInt64Tensor b, OperationKind kind, string name)
    {
        ValidateSameShapeBinary(a, b, name);
        var executable = GetOrCompile(OperationKey.Binary(kind, a.Rows, a.Cols));
        return new PjrtInt64Tensor(this, executable.Execute(a.Buffer, b.Buffer), a.Rows, a.Cols, PjrtElementType.Int64);
    }

    private PjrtLoadedExecutable GetOrCompile(OperationKey key) => _executableCache.GetOrCompile(key);

    private static string StableHlo(OperationKey key) => key.Kind switch
    {
        OperationKind.MatMul => StableHloProgram.MatMul(key.Rows, key.Cols, key.OutCols, PjrtElementType.Int64),
        OperationKind.Add => StableHloProgram.Binary(key.Rows, key.Cols, "stablehlo.add", PjrtElementType.Int64),
        OperationKind.Subtract => StableHloProgram.Binary(key.Rows, key.Cols, "stablehlo.subtract", PjrtElementType.Int64),
        OperationKind.Multiply => StableHloProgram.Binary(key.Rows, key.Cols, "stablehlo.multiply", PjrtElementType.Int64),
        OperationKind.Negate => StableHloProgram.Unary(key.Rows, key.Cols, "stablehlo.negate", PjrtElementType.Int64),
        OperationKind.Sum => StableHloProgram.Sum(key.Rows, key.Cols, PjrtElementType.Int64),
        OperationKind.Transpose => StableHloProgram.Transpose(key.Rows, key.Cols, PjrtElementType.Int64),
        OperationKind.Scale => StableHloProgram.Scale(key.Rows, key.Cols, key.Scalar, PjrtElementType.Int64),
        _ => throw new NotSupportedException($"Unsupported XLA operation: {key.Kind}")
    };

    private void ValidateUnary(PjrtInt64Tensor value, string operationName)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!ReferenceEquals(value.Owner, this))
            throw new ArgumentException("Tensor must be owned by this backend.", nameof(value));
        if (value.ElementType != PjrtElementType.Int64)
            throw new NotSupportedException($"Only Int64 {operationName} is supported.");
    }

    private void ValidateOwnedBinary(PjrtInt64Tensor a, PjrtInt64Tensor b, string operationName)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (!ReferenceEquals(a.Owner, this) || !ReferenceEquals(b.Owner, this))
            throw new ArgumentException("Both tensors must be owned by this backend.");
        if (a.ElementType != PjrtElementType.Int64 || b.ElementType != PjrtElementType.Int64)
            throw new NotSupportedException($"Only Int64 {operationName} is supported.");
    }

    private void ValidateSameShapeBinary(PjrtInt64Tensor a, PjrtInt64Tensor b, string operationName)
    {
        ValidateOwnedBinary(a, b, operationName);
        if (a.Rows != b.Rows || a.Cols != b.Cols)
            throw new ArgumentException($"Cannot {operationName} tensors with shapes {a.Rows}x{a.Cols} and {b.Rows}x{b.Cols}.");
    }

    private PjrtInt64Tensor RequirePjrtFloatTensor(Helium.Hardware.IHardwareTensor<long> tensor, string parameterName)
    {
        ThrowIfDisposed();
        if (tensor is not PjrtInt64Tensor xlaTensor || !ReferenceEquals(xlaTensor.Owner, this))
            throw new ArgumentException("Tensor must be owned by this XLA backend.", parameterName);
        return xlaTensor;
    }

    private readonly record struct OperationKey(OperationKind Kind, int Rows, int Cols, int OutCols, long Scalar = 0)
    {
        public static OperationKey MatMul(int rows, int cols, int outCols) => new(OperationKind.MatMul, rows, cols, outCols);
        public static OperationKey Binary(OperationKind kind, int rows, int cols) => new(kind, rows, cols, cols);
        public static OperationKey Unary(OperationKind kind, int rows, int cols) => new(kind, rows, cols, cols);
        public static OperationKey Scale(int rows, int cols, long scalar) => new(OperationKind.Scale, rows, cols, cols, scalar);
    }

    private enum OperationKind { MatMul, Add, Subtract, Multiply, Negate, Sum, Transpose, Scale }
}
